using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    // End-to-end validation of the exact pieces intended for the automatic
    // mission cycle. Every route and transaction must finish with verified
    // evidence before the next phase starts; failures are never retried.
    internal sealed class AutomaticLogisticsCycle : IDisposable
    {
        private const int MaximumSellBagRefreshUses = 3;
        private enum Phase
        {
            Idle,
            LoadingInventory,
            StartBank,
            TravelBank,
            Bank,
            ReturnBank,
            StartShop,
            TravelShop,
            LoadingShopInventory,
            Shop,
            ReturnShop
        }

        private readonly LogisticsRouteNavigator _route;
        private readonly BankTransactions _bank;
        private readonly ShopSaleTest _shop;
        private readonly Action<string> _say;
        private Phase _phase;
        private DateTime _nextTick;
        private DateTime _inventoryLoadStarted;
        private string _site;
        private int _maximumItems, _stored, _sold, _plannedRejects;
        private bool _bankPlanned, _shopPlanned, _inventoryLoadRequested;
        private string _deferredFailure;
        private readonly HashSet<Identity> _pendingSellBags = new HashSet<Identity>();
        private readonly Dictionary<Identity, int> _sellBagRefreshUses = new Dictionary<Identity, int>();
        private DateTime _lastSellBagRefreshUse;

        public bool IsActive => _phase != Phase.Idle;
        public VerifiedOperationResult Result { get; private set; }
        public string LastFailure { get; private set; }
        public string Status => IsActive
            ? $"{_site} {_phase}, stored={_stored}, sold={_sold}, limit={_maximumItems}"
            : Result == VerifiedOperationResult.Failed
                ? "failed: " + LastFailure
                : Result == VerifiedOperationResult.Succeeded ? "complete" : "inactive";

        public AutomaticLogisticsCycle(LogisticsRouteNavigator route, BankTransactions bank,
            ShopSaleTest shop, Action<string> say)
        {
            _route = route;
            _bank = bank;
            _shop = shop;
            _say = say;
            Game.OnUpdate += OnUpdate;
            Inventory.ContainerOpened += OnContainerOpened;
        }

        public bool Start(int maximumItems)
        {
            if (IsActive)
            { _say("An automatic logistics cycle is already active."); return false; }
            if (maximumItems < 1 || maximumItems > 20)
            { _say("Automatic logistics item limit must be between 1 and 20."); return false; }
            if (Game.IsZoning || DynelManager.LocalPlayer == null)
            { _say("Automatic logistics held while zoning or before the player is available."); return false; }
            if (_route.IsActive || _bank.IsActive || _shop.IsActive)
            { _say("Finish or stop the current logistics route or transaction first."); return false; }
            _site = _route.FindVerifiedOriginSite();
            if (string.IsNullOrEmpty(_site))
            {
                LastFailure = "no unique surveyed site is verified at the current mission terminal";
                Result = VerifiedOperationResult.Failed;
                _say("Automatic logistics held: stand on foot beside one exact surveyed OA, Borealis, or ICC mission terminal.");
                return false;
            }
            _maximumItems = maximumItems;
            _stored = _sold = 0;
            _plannedRejects = 0;
            _bankPlanned = _shopPlanned = false;
            _inventoryLoadRequested = false;
            _inventoryLoadStarted = DateTime.UtcNow;
            ClearSellBagLoading();
            _deferredFailure = null;
            LastFailure = null;
            Result = VerifiedOperationResult.Running;
            _phase = Phase.LoadingInventory;
            _nextTick = DateTime.MinValue;
            _say($"Automatic logistics validation started at {_site}: loading RKM Sell bag snapshots before planning up to " +
                $"{maximumItems} item(s) per destination.");
            return true;
        }

        public void Stop(bool silent = false)
        {
            if (!IsActive) return;
            _bank.Stop();
            _shop.Stop();
            _route.Stop(true);
            ClearSellBagLoading();
            _phase = Phase.Idle;
            Result = VerifiedOperationResult.Failed;
            LastFailure = "cycle stopped before verified completion";
            if (!silent) _say("Automatic logistics cycle stopped; inspect any transaction named in the preceding message before retrying.");
        }

        private void OnUpdate(object sender, float elapsed)
        {
            if (!IsActive || Game.IsZoning || DynelManager.LocalPlayer == null ||
                DateTime.UtcNow < _nextTick) return;
            _nextTick = DateTime.UtcNow.AddMilliseconds(200);
            try { Tick(); }
            catch (Exception ex) { Fail("cycle error: " + ex.Message); }
        }

        private void Tick()
        {
            switch (_phase)
            {
                case Phase.LoadingInventory:
                    if (!_inventoryLoadRequested)
                    {
                        if (Item.HasPendingUse || Spell.HasPendingCast)
                        {
                            if (DateTime.UtcNow - _inventoryLoadStarted > TimeSpan.FromSeconds(10))
                                Fail("another item use or spell remained pending before inventory loading");
                            return;
                        }
                        if (!RequestSellBagSnapshots(out int requested, out string error))
                        { Fail(error); return; }
                        _inventoryLoadRequested = true;
                        _inventoryLoadStarted = DateTime.UtcNow;
                        _say($"Automatic logistics: requested {requested} RKM Sell bag snapshot refresh(es); " +
                            $"waiting for {_pendingSellBags.Count} ContainerOpened confirmation(s).");
                        return;
                    }
                    if (!SellBagSnapshotsSettled(out string loadError) ||
                        Item.HasPendingUse || Spell.HasPendingCast)
                    {
                        if (!string.IsNullOrEmpty(loadError)) { Fail(loadError); return; }
                        if (DateTime.UtcNow - _inventoryLoadStarted > TimeSpan.FromSeconds(10))
                            Fail($"RKM Sell bag loading did not settle; {_pendingSellBags.Count} container-open confirmation(s) missing");
                        return;
                    }
                    if (DateTime.UtcNow - _inventoryLoadStarted < TimeSpan.FromMilliseconds(250)) return;
                    int keepPlan = _bank.CountStorableKeepItems();
                    int rejectPlan = _shop.CountEligibleRejectItems();
                    _plannedRejects = rejectPlan;
                    _bankPlanned = keepPlan > 0;
                    _shopPlanned = rejectPlan > 0;
                    _say($"Automatic logistics plan at {_site}: bank Keep={keepPlan}, shop Reject={rejectPlan}, " +
                        $"limit={_maximumItems} per destination; {_shop.SellInventorySummary()}.");
                    if (_bankPlanned) _phase = Phase.StartBank;
                    else if (_shopPlanned) _phase = Phase.StartShop;
                    else Complete();
                    return;
                case Phase.StartBank:
                    if (!_route.Start(_site, "bank", true))
                    { Fail("bank route did not start: " + (_route.LastFailure ?? "unknown reason")); return; }
                    _phase = Phase.TravelBank;
                    return;
                case Phase.TravelBank:
                    if (RouteFailed("bank outward route")) return;
                    if (!_route.AtTarget) return;
                    int keep = Math.Min(_maximumItems, _bank.CountStorableKeepItems());
                    if (keep > 0)
                    {
                        if (!_bank.StartStore(keep, true))
                        { Fail("bank storage did not start: " + (_bank.LastFailure ?? "unknown reason")); return; }
                        _phase = Phase.Bank;
                    }
                    else BeginReturn(Phase.ReturnBank, "bank");
                    return;
                case Phase.Bank:
                    if (_bank.IsActive) return;
                    if (_bank.Result != VerifiedOperationResult.Succeeded)
                    { Fail("bank storage was not verified: " + (_bank.LastFailure ?? "unknown reason")); return; }
                    _stored += _bank.CompletedCount;
                    BeginReturn(Phase.ReturnBank, "bank");
                    return;
                case Phase.ReturnBank:
                    if (RouteFailed("bank return route")) return;
                    if (_route.IsActive) return;
                    if (_route.Result != VerifiedOperationResult.Succeeded) return;
                    if (_shopPlanned) _phase = Phase.StartShop;
                    else Complete();
                    return;
                case Phase.StartShop:
                    if (!_route.Start(_site, "shop", true))
                    { Fail("shop route did not start: " + (_route.LastFailure ?? "unknown reason")); return; }
                    _phase = Phase.TravelShop;
                    return;
                case Phase.TravelShop:
                    if (RouteFailed("shop outward route")) return;
                    if (!_route.AtTarget) return;
                    // Zoning clears unopened backpack snapshots. Refresh RKM
                    // Sell at the shop before selecting a destructive sale.
                    _inventoryLoadRequested = false;
                    _inventoryLoadStarted = DateTime.UtcNow;
                    _phase = Phase.LoadingShopInventory;
                    return;
                case Phase.LoadingShopInventory:
                    if (!_inventoryLoadRequested)
                    {
                        if (Item.HasPendingUse || Spell.HasPendingCast)
                        {
                            if (DateTime.UtcNow - _inventoryLoadStarted > TimeSpan.FromSeconds(10))
                                Fail("another item use or spell remained pending before shop inventory loading");
                            return;
                        }
                        if (!RequestSellBagSnapshots(out int requested, out string error))
                        { Fail(error); return; }
                        _inventoryLoadRequested = true;
                        _inventoryLoadStarted = DateTime.UtcNow;
                        _say($"Automatic logistics at shop: requested {requested} RKM Sell bag snapshot refresh(es) after zoning; " +
                            $"waiting for {_pendingSellBags.Count} ContainerOpened confirmation(s).");
                        return;
                    }
                    if (!SellBagSnapshotsSettled(out string shopLoadError) ||
                        Item.HasPendingUse || Spell.HasPendingCast)
                    {
                        if (!string.IsNullOrEmpty(shopLoadError)) { Fail(shopLoadError); return; }
                        if (DateTime.UtcNow - _inventoryLoadStarted > TimeSpan.FromSeconds(10))
                            Fail($"post-zone RKM Sell bag loading did not settle at the shop; " +
                                $"{_pendingSellBags.Count} container-open confirmation(s) missing");
                        return;
                    }
                    if (DateTime.UtcNow - _inventoryLoadStarted < TimeSpan.FromMilliseconds(250)) return;
                    int reject = Math.Min(_maximumItems, _shop.CountEligibleRejectItems());
                    if (reject > 0)
                    {
                        if (!_shop.Start(reject, true))
                        { Fail("shop sale did not start: " + (_shop.LastFailure ?? "unknown reason")); return; }
                        _phase = Phase.Shop;
                    }
                    else
                    {
                        _deferredFailure = _plannedRejects > 0
                            ? $"planned {_plannedRejects} RKM Sell Reject item(s), but none were observed after the required post-zone bag refresh"
                            : "no eligible RKM Sell Reject item remained at the shop";
                        _say("Automatic logistics sale held: " + _deferredFailure + ". Returning to the mission terminal without opening a trade.");
                        BeginReturn(Phase.ReturnShop, "shop");
                    }
                    return;
                case Phase.Shop:
                    if (_shop.IsActive) return;
                    if (_shop.Result != VerifiedOperationResult.Succeeded)
                    { Fail("shop sale was not verified: " + (_shop.LastFailure ?? "unknown reason")); return; }
                    _sold += _shop.CompletedCount;
                    BeginReturn(Phase.ReturnShop, "shop");
                    return;
                case Phase.ReturnShop:
                    if (RouteFailed("shop return route")) return;
                    if (_route.IsActive) return;
                    if (_route.Result == VerifiedOperationResult.Succeeded)
                    {
                        if (!string.IsNullOrEmpty(_deferredFailure))
                            Fail("shop phase was not completed after verified return: " + _deferredFailure);
                        else Complete();
                    }
                    return;
            }
        }

        private bool RouteFailed(string phase)
        {
            if (_route.Result != VerifiedOperationResult.Failed) return false;
            Fail(phase + " failed: " + (_route.LastFailure ?? "unknown reason"));
            return true;
        }

        private bool RequestSellBagSnapshots(out int requested, out string error)
        {
            requested = 0;
            error = null;
            ClearSellBagLoading();
            var bags = Inventory.Backpacks
                .Where(bag => ManagerLoot.ManagedBagFamily.Matches(bag.Name,
                    ManagerLoot.ManagedBagFamily.Sell))
                .ToArray();
            foreach (Container bag in bags)
            {
                int observed = Inventory.GetContainerItems(bag.Identity).Count(x => x != null);
                // IsOpen can survive a zone/plugin reload while the item list is
                // empty. Only a non-empty open snapshot is safe to reuse.
                if (bag.IsOpen && observed > 0) continue;
                Item item = Inventory.Items.FirstOrDefault(candidate => candidate != null &&
                    candidate.UniqueIdentity == bag.Identity);
                if (item == null)
                {
                    error = $"RKM Sell bag {bag.Identity} has no matching inventory item to open";
                    _pendingSellBags.Clear();
                    return false;
                }
                _pendingSellBags.Add(bag.Identity);
                _sellBagRefreshUses[bag.Identity] = 1;
                item.Use();
                _lastSellBagRefreshUse = DateTime.UtcNow;
                requested++;
            }
            return true;
        }

        private bool SellBagSnapshotsSettled(out string error)
        {
            error = null;
            foreach (Identity identity in _pendingSellBags.ToArray())
            {
                if (Inventory.GetContainerItems(identity).Any(x => x != null))
                {
                    _pendingSellBags.Remove(identity);
                    _sellBagRefreshUses.Remove(identity);
                    _say($"Automatic logistics: RKM Sell bag {identity} item snapshot arrived; " +
                        $"{_pendingSellBags.Count} pending.");
                }
            }
            if (_pendingSellBags.Count == 0) return true;
            if (Item.HasPendingUse || Spell.HasPendingCast ||
                DateTime.UtcNow - _lastSellBagRefreshUse < TimeSpan.FromMilliseconds(600)) return false;
            Identity retry = _pendingSellBags.FirstOrDefault(identity =>
                _sellBagRefreshUses.TryGetValue(identity, out int uses) &&
                uses < MaximumSellBagRefreshUses);
            if (retry == Identity.None) return false;
            Item item = Inventory.Items.FirstOrDefault(candidate => candidate != null &&
                candidate.UniqueIdentity == retry);
            if (item == null)
            {
                error = $"RKM Sell bag {retry} disappeared while refreshing its item snapshot";
                return false;
            }
            int nextUse = _sellBagRefreshUses[retry] + 1;
            _sellBagRefreshUses[retry] = nextUse;
            item.Use();
            _lastSellBagRefreshUse = DateTime.UtcNow;
            _say($"Automatic logistics: RKM Sell bag {retry} still has no confirmed item snapshot; " +
                $"bounded refresh use {nextUse}/{MaximumSellBagRefreshUses} sent.");
            return false;
        }

        private void OnContainerOpened(object sender, Container container)
        {
            if (container != null && _pendingSellBags.Remove(container.Identity))
            {
                _sellBagRefreshUses.Remove(container.Identity);
                _say($"Automatic logistics: RKM Sell bag {container.Identity} snapshot opened; " +
                    $"{_pendingSellBags.Count} pending.");
            }
        }

        private void ClearSellBagLoading()
        {
            _pendingSellBags.Clear();
            _sellBagRefreshUses.Clear();
            _lastSellBagRefreshUse = DateTime.MinValue;
        }

        private void BeginReturn(Phase returnPhase, string purpose)
        {
            if (!_route.Return())
            { Fail(purpose + " return route did not start: " + (_route.LastFailure ?? "unknown reason")); return; }
            _phase = returnPhase;
        }

        private void Complete()
        {
            _phase = Phase.Idle;
            ClearSellBagLoading();
            Result = VerifiedOperationResult.Succeeded;
            LastFailure = null;
            _say($"Automatic logistics validation complete at {_site}: {_stored} Keep item(s) stored, " +
                $"{_sold} Reject item(s) sold, main free slots={Inventory.NumFreeSlots}; returned to the surveyed mission terminal.");
        }

        private void Fail(string reason)
        {
            _bank.Stop();
            _shop.Stop();
            _route.Stop(true);
            ClearSellBagLoading();
            _phase = Phase.Idle;
            Result = VerifiedOperationResult.Failed;
            LastFailure = reason;
            _say("Automatic logistics stopped: " + reason + ". No failed route, transfer, or trade action was retried.");
        }

        public void Dispose()
        {
            Game.OnUpdate -= OnUpdate;
            Inventory.ContainerOpened -= OnContainerOpened;
            Stop(true);
        }
    }
}
