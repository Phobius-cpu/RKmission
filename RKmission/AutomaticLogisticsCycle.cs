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
            Grounding,
            LoadingInventory,
            NormalizeInventory,
            StartBank,
            TravelBank,
            Bank,
            ReturnBank,
            StartShop,
            TravelShop,
            LoadingShopInventory,
            Shop,
            ReturnShop,
            Reconcile
        }

        private readonly LogisticsRouteNavigator _route;
        private readonly BankTransactions _bank;
        private readonly ShopSaleTest _shop;
        private readonly ManagedInventoryNormalizer _normalizer;
        private readonly Action<string> _say;
        private Phase _phase;
        private DateTime _nextTick;
        private DateTime _inventoryLoadStarted;
        private DateTime _groundingStarted;
        private string _site;
        private int _maximumItems, _stored, _sold, _plannedBankKeeps, _expectedRemainingBankKeeps;
        private int _plannedRejects, _expectedRemainingRejects;
        private bool _bankPlanned, _shopPlanned, _inventoryLoadRequested, _missionCycleRun;
        private bool _groundRequested;
        private string _deferredFailure;
        private readonly HashSet<Identity> _pendingSellBags = new HashSet<Identity>();
        private readonly Dictionary<Identity, int> _sellBagRefreshUses = new Dictionary<Identity, int>();
        private DateTime _lastSellBagRefreshUse;

        public bool IsActive => _phase != Phase.Idle;
        public VerifiedOperationResult Result { get; private set; }
        public string LastFailure { get; private set; }
        public int StoredCount => _stored;
        public int SoldCount => _sold;
        public string Status => IsActive
            ? $"{_site} {_phase}, stored={_stored}, sold={_sold}, limit={_maximumItems}"
            : Result == VerifiedOperationResult.Failed
                ? "failed: " + LastFailure
                : Result == VerifiedOperationResult.Succeeded ? "complete" : "inactive";

        public AutomaticLogisticsCycle(LogisticsRouteNavigator route, BankTransactions bank,
            ShopSaleTest shop, ManagerLoot.ManagerLoot loot, InventoryPolicy policy, Action<string> say)
        {
            _route = route;
            _bank = bank;
            _shop = shop;
            _normalizer = new ManagedInventoryNormalizer(loot, policy, say);
            _say = say;
            Game.OnUpdate += OnUpdate;
            Inventory.ContainerOpened += OnContainerOpened;
        }

        public bool Start(int maximumItems, bool missionCycleRun = false)
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
            _missionCycleRun = missionCycleRun;
            if (!missionCycleRun && DynelManager.LocalPlayer.MovementState == MovementState.Fly)
            {
                LastFailure = "manual automatic-logistics validation must start on foot";
                Result = VerifiedOperationResult.Failed;
                _say("Automatic logistics validation held: land beside the exact surveyed terminal first.");
                return false;
            }
            _stored = _sold = 0;
            _plannedBankKeeps = _expectedRemainingBankKeeps = 0;
            _plannedRejects = _expectedRemainingRejects = 0;
            _bankPlanned = _shopPlanned = false;
            _inventoryLoadRequested = false;
            _inventoryLoadStarted = DateTime.UtcNow;
            ClearSellBagLoading();
            _deferredFailure = null;
            LastFailure = null;
            Result = VerifiedOperationResult.Running;
            _groundingStarted = DateTime.UtcNow;
            _groundRequested = false;
            _phase = missionCycleRun ? Phase.Grounding : Phase.LoadingInventory;
            _nextTick = DateTime.MinValue;
            _say($"{(_missionCycleRun ? "Automatic mission-cycle logistics" : "Automatic logistics validation")} " +
                $"started at {_site}: loading RK Loot/RK Junk bag snapshots before planning up to " +
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
                case Phase.Grounding:
                    if (!_route.TryGroundAtVerifiedOrigin(_site, !_groundRequested,
                        out bool onFoot, out string groundFailure))
                    { Fail("automatic ground-state handoff failed: " + groundFailure); return; }
                    if (!_groundRequested && DynelManager.LocalPlayer.MovementState == MovementState.Fly)
                    {
                        _groundRequested = true;
                        _say($"Automatic logistics at {_site}: verified surveyed origin while flying; " +
                            "sent one LeaveFly transition and waiting for AO# to report on-foot.");
                    }
                    if (onFoot)
                    {
                        _phase = Phase.LoadingInventory;
                        _inventoryLoadStarted = DateTime.UtcNow;
                        _say($"Automatic logistics at {_site}: on-foot state and exact surveyed origin verified.");
                    }
                    else if (DateTime.UtcNow - _groundingStarted > TimeSpan.FromSeconds(15))
                        Fail("automatic ground-state handoff timed out after 15 seconds; no route start was attempted");
                    return;
                case Phase.LoadingInventory:
                    if (!_inventoryLoadRequested)
                    {
                        if (Item.HasPendingUse || Spell.HasPendingCast)
                        {
                            if (DateTime.UtcNow - _inventoryLoadStarted > TimeSpan.FromSeconds(10))
                                Fail("another item use or spell remained pending before inventory loading");
                            return;
                        }
                        if (!RequestManagedBagSnapshots(true, out int requested, out string error))
                        { Fail(error); return; }
                        _inventoryLoadRequested = true;
                        _inventoryLoadStarted = DateTime.UtcNow;
                        _say($"Automatic logistics: requested {requested} RK Loot/RK Junk managed-bag snapshot refresh(es); " +
                            $"waiting for {_pendingSellBags.Count} ContainerOpened confirmation(s).");
                        return;
                    }
                    if (!SellBagSnapshotsSettled(out string loadError) ||
                        Item.HasPendingUse || Spell.HasPendingCast)
                    {
                        if (!string.IsNullOrEmpty(loadError)) { Fail(loadError); return; }
                        if (DateTime.UtcNow - _inventoryLoadStarted > TimeSpan.FromSeconds(10))
                            Fail($"managed bag loading did not settle; {_pendingSellBags.Count} container-open confirmation(s) missing");
                        return;
                    }
                    if (DateTime.UtcNow - _inventoryLoadStarted < TimeSpan.FromMilliseconds(250)) return;
                    _normalizer.Start(_maximumItems);
                    _phase = Phase.NormalizeInventory;
                    return;
                case Phase.NormalizeInventory:
                    _normalizer.Tick();
                    if (_normalizer.Failed) { Fail("managed inventory normalization failed: " + _normalizer.Failure); return; }
                    if (_normalizer.IsActive) return;
                    int bankEligible = _normalizer.BankReservations.Count;
                    int totalMainBankEligible = _normalizer.MainBankEligibleCount();
                    int rejectEligible = _shop.CountEligibleRejectItems();
                    _plannedBankKeeps = Math.Min(_maximumItems, bankEligible);
                    _expectedRemainingBankKeeps = Math.Max(0,
                        totalMainBankEligible - _plannedBankKeeps);
                    _plannedRejects = Math.Min(_maximumItems, rejectEligible);
                    _expectedRemainingRejects = rejectEligible - _plannedRejects;
                    _bankPlanned = _plannedBankKeeps > 0;
                    _shopPlanned = _plannedRejects > 0;
                    _say($"Automatic logistics plan at {_site}: bank Keep={_plannedBankKeeps}, shop Reject={_plannedRejects}, " +
                        $"limit={_maximumItems} per destination; {_shop.SellInventorySummary()}.");
                    if (_bankPlanned) _phase = Phase.StartBank;
                    else if (_shopPlanned) _phase = Phase.StartShop;
                    else BeginReconciliation();
                    return;
                case Phase.StartBank:
                    if (!_route.Start(_site, "bank", true))
                    { Fail("bank route did not start: " + (_route.LastFailure ?? "unknown reason")); return; }
                    _phase = Phase.TravelBank;
                    return;
                case Phase.TravelBank:
                    if (RouteFailed("bank outward route")) return;
                    if (!_route.AtTarget) return;
                    if (!_bank.TryValidateReservations(_normalizer.BankReservations,
                        _plannedBankKeeps, out string reservationError))
                    { Fail(reservationError + "; bank transaction was held"); return; }
                    if (_plannedBankKeeps > 0)
                    {
                        if (!_bank.StartReservedStore(_plannedBankKeeps, _normalizer.BankReservations))
                        { Fail("bank storage did not start: " + (_bank.LastFailure ?? "unknown reason")); return; }
                        _phase = Phase.Bank;
                    }
                    else BeginReturn(Phase.ReturnBank, "bank");
                    return;
                case Phase.Bank:
                    if (_bank.IsActive) return;
                    if (_bank.Result != VerifiedOperationResult.Succeeded)
                    { Fail("bank storage was not verified: " + (_bank.LastFailure ?? "unknown reason")); return; }
                    if (_normalizer.BankReservations.Count != 0)
                    { Fail($"bank storage completed with {_normalizer.BankReservations.Count} unconsumed Keep reservation(s)"); return; }
                    _stored += _bank.CompletedCount;
                    BeginReturn(Phase.ReturnBank, "bank");
                    return;
                case Phase.ReturnBank:
                    if (RouteFailed("bank return route")) return;
                    if (_route.IsActive) return;
                    if (_route.Result != VerifiedOperationResult.Succeeded) return;
                    if (_shopPlanned) _phase = Phase.StartShop;
                    else BeginReconciliation();
                    return;
                case Phase.StartShop:
                    int unsafeKeep = _normalizer.MisplacedKeepCount();
                    if (unsafeKeep > 0)
                    { Fail($"shop phase held: {unsafeKeep} known Keep item(s) remain in RK Junk"); return; }
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
                        if (!RequestManagedBagSnapshots(false, out int requested, out string error))
                        { Fail(error); return; }
                        _inventoryLoadRequested = true;
                        _inventoryLoadStarted = DateTime.UtcNow;
                        _say($"Automatic logistics at shop: requested {requested} RK Junk bag snapshot refresh(es) after zoning; " +
                            $"waiting for {_pendingSellBags.Count} ContainerOpened confirmation(s).");
                        return;
                    }
                    if (!SellBagSnapshotsSettled(out string shopLoadError) ||
                        Item.HasPendingUse || Spell.HasPendingCast)
                    {
                        if (!string.IsNullOrEmpty(shopLoadError)) { Fail(shopLoadError); return; }
                        if (DateTime.UtcNow - _inventoryLoadStarted > TimeSpan.FromSeconds(10))
                            Fail($"post-zone RK Junk bag loading did not settle at the shop; " +
                                $"{_pendingSellBags.Count} container-open confirmation(s) missing");
                        return;
                    }
                    if (DateTime.UtcNow - _inventoryLoadStarted < TimeSpan.FromMilliseconds(250)) return;
                    int shopUnsafeKeep = _normalizer.MisplacedKeepCount();
                    if (shopUnsafeKeep > 0)
                    { Fail($"shop phase held after snapshot refresh: {shopUnsafeKeep} known Keep item(s) remain in RK Junk"); return; }
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
                            ? $"planned {_plannedRejects} RK Junk Reject item(s), but none were observed after the required post-zone bag refresh"
                            : "no eligible RK Junk Reject item remained at the shop";
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
                        else BeginReconciliation();
                    }
                    return;
                case Phase.Reconcile:
                    if (Game.IsZoning || DynelManager.LocalPlayer == null) return;
                    if (!string.Equals(_route.FindVerifiedOriginSite(), _site,
                        StringComparison.OrdinalIgnoreCase))
                    { Fail("post-logistics reconciliation lost the exact surveyed origin"); return; }
                    if (Item.HasPendingUse || Spell.HasPendingCast) return;
                    int bankable = _bank.CountStorableKeepItems();
                    int baggedBankable = _normalizer.BaggedBankEligibleCount();
                    int misplaced = _normalizer.MisplacedKeepCount();
                    int saleEligible = _shop.CountEligibleRejectItems();
                    int unprocessedPlannedBankKeeps = _normalizer.BankReservations.Count;
                    int unprocessedRequestedRejects = Math.Max(0, saleEligible - _expectedRemainingRejects);
                    if (_normalizer.HasPendingTransfer || baggedBankable > 0 || misplaced > 0 ||
                        _stored < _plannedBankKeeps || unprocessedPlannedBankKeeps > 0 ||
                        _sold < _plannedRejects || unprocessedRequestedRejects > 0)
                    { Fail($"final reconciliation held: bank-eligible Keep in main={bankable}, in bags={baggedBankable}, Keep in RK Junk={misplaced}, unprocessed planned bank Keep={unprocessedPlannedBankKeeps}, extra bank Keep beyond requested batch={Math.Min(bankable, _expectedRemainingBankKeeps)}, unprocessed requested Reject={unprocessedRequestedRejects}, extra Reject beyond requested batch={Math.Min(saleEligible, _expectedRemainingRejects)}, normalization pending={_normalizer.HasPendingTransfer}"); return; }
                    _say($"Post-logistics reconciliation verified at {_site}: Keep in RK Junk=0, bank Keep reservations=0, " +
                        $"unprocessed planned bank Keep=0, extra bank-eligible Keep beyond requested batch={bankable} (expected), " +
                        $"unprocessed requested Reject=0, extra Reject beyond requested batch={saleEligible} (expected); no transfer or destructive action is pending.");
                    Complete();
                    return;
            }
        }

        private void BeginReconciliation()
        {
            _phase = Phase.Reconcile;
            _nextTick = DateTime.UtcNow.AddMilliseconds(250);
        }

        private bool RouteFailed(string phase)
        {
            if (_route.Result != VerifiedOperationResult.Failed) return false;
            Fail(phase + " failed: " + (_route.LastFailure ?? "unknown reason"));
            return true;
        }

        private bool RequestManagedBagSnapshots(bool includeKeep, out int requested, out string error)
        {
            requested = 0;
            error = null;
            ClearSellBagLoading();
            var bags = Inventory.Backpacks
                .Where(bag => ManagerLoot.ManagedBagFamily.IsSell(bag.Name) ||
                    (includeKeep && ManagerLoot.ManagedBagFamily.IsKeep(bag.Name)))
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
                    error = $"managed bag {bag.Identity} has no matching inventory item to open";
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
                    _say($"Automatic logistics: managed bag {identity} item snapshot arrived; " +
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
                error = $"managed bag {retry} disappeared while refreshing its item snapshot";
                return false;
            }
            int nextUse = _sellBagRefreshUses[retry] + 1;
            _sellBagRefreshUses[retry] = nextUse;
            item.Use();
            _lastSellBagRefreshUse = DateTime.UtcNow;
            _say($"Automatic logistics: managed bag {retry} still has no confirmed item snapshot; " +
                $"bounded refresh use {nextUse}/{MaximumSellBagRefreshUses} sent.");
            return false;
        }

        private void OnContainerOpened(object sender, Container container)
        {
            if (container != null && _pendingSellBags.Remove(container.Identity))
            {
                _sellBagRefreshUses.Remove(container.Identity);
                _say($"Automatic logistics: managed bag {container.Identity} snapshot opened; " +
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
            _say($"{(_missionCycleRun ? "Automatic mission-cycle logistics" : "Automatic logistics validation")} " +
                $"complete at {_site}: {_stored} Keep item(s) stored, " +
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
