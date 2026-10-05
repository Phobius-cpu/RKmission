using System;
using System.Linq;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    // End-to-end validation of the exact pieces intended for the automatic
    // mission cycle. Every route and transaction must finish with verified
    // evidence before the next phase starts; failures are never retried.
    internal sealed class AutomaticLogisticsCycle : IDisposable
    {
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
        private int _maximumItems, _stored, _sold;
        private bool _bankPlanned, _shopPlanned, _inventoryLoadRequested;

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
            _bankPlanned = _shopPlanned = false;
            _inventoryLoadRequested = false;
            _inventoryLoadStarted = DateTime.UtcNow;
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
                        int opened = RequestSellBagSnapshots();
                        _inventoryLoadRequested = true;
                        _inventoryLoadStarted = DateTime.UtcNow;
                        _say($"Automatic logistics: requested inventory snapshots for {opened} RKM Sell bag(s).");
                        return;
                    }
                    if (Item.HasPendingUse || Spell.HasPendingCast)
                    {
                        if (DateTime.UtcNow - _inventoryLoadStarted > TimeSpan.FromSeconds(10))
                            Fail("RKM Sell bag loading did not settle");
                        return;
                    }
                    if (DateTime.UtcNow - _inventoryLoadStarted < TimeSpan.FromSeconds(1)) return;
                    int keepPlan = _bank.CountStorableKeepItems();
                    int rejectPlan = _shop.CountEligibleRejectItems();
                    _bankPlanned = keepPlan > 0;
                    _shopPlanned = rejectPlan > 0;
                    _say($"Automatic logistics plan at {_site}: bank Keep={keepPlan}, shop Reject={rejectPlan}, " +
                        $"limit={_maximumItems} per destination.");
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
                    int reject = Math.Min(_maximumItems, _shop.CountEligibleRejectItems());
                    if (reject > 0)
                    {
                        if (!_shop.Start(reject, true))
                        { Fail("shop sale did not start: " + (_shop.LastFailure ?? "unknown reason")); return; }
                        _phase = Phase.Shop;
                    }
                    else BeginReturn(Phase.ReturnShop, "shop");
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
                    if (_route.Result == VerifiedOperationResult.Succeeded) Complete();
                    return;
            }
        }

        private bool RouteFailed(string phase)
        {
            if (_route.Result != VerifiedOperationResult.Failed) return false;
            Fail(phase + " failed: " + (_route.LastFailure ?? "unknown reason"));
            return true;
        }

        private static int RequestSellBagSnapshots()
        {
            var bagIds = Inventory.Backpacks
                .Where(bag => ManagerLoot.ManagedBagFamily.Matches(bag.Name,
                    ManagerLoot.ManagedBagFamily.Sell))
                .Select(bag => bag.Identity).ToArray();
            int opened = 0;
            foreach (Item item in Inventory.Items.Where(item => item != null &&
                bagIds.Any(bag => bag.Instance == item.UniqueIdentity.Instance)))
            {
                item.Use();
                item.Use();
                opened++;
            }
            return opened;
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
            _phase = Phase.Idle;
            Result = VerifiedOperationResult.Failed;
            LastFailure = reason;
            _say("Automatic logistics stopped: " + reason + ". No failed route, transfer, or trade action was retried.");
        }

        public void Dispose()
        {
            Game.OnUpdate -= OnUpdate;
            Stop(true);
        }
    }
}
