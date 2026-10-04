using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using Newtonsoft.Json;

namespace RKmission
{
    // Narrow, opt-in packet trace adapted from knows-helpers/N3Inspector.
    // It records bank/shop messages and manually observed building transitions
    // without enabling travel or transactions itself.
    internal sealed class LogisticsProbe : IDisposable
    {
        private sealed class Observation
        {
            public DateTime AtUtc { get; set; }
            public string Event { get; set; }
            public string Site { get; set; }
            public string Purpose { get; set; }
            public int Playfield { get; set; }
            public float[] Position { get; set; }
            public string MissionTerminalIdentity { get; set; }
            public float[] MissionTerminalPosition { get; set; }
            public int DestinationPlayfield { get; set; }
            public float[] DestinationPosition { get; set; }
            public string Details { get; set; }
            public string[] NearbyTerminals { get; set; }
        }

        private readonly Action<string> _say;
        private readonly ManagerLoot.ManagerLoot _loot;
        private readonly string _directory;
        private readonly List<Observation> _observations = new List<Observation>();
        private Observation _pendingZone;
        private Observation _lastStable;
        private bool _zoneEndedPending;
        private DateTime _nextStableSample;
        private string _site, _purpose;
        private int _pathPlayfield;
        private Vector3 _lastPathPoint;
        private int _messages;
        private int _rawBankMessages, _bankChanges;
        private bool _bankWasOpen, _bankBaselineReady;
        private DateTime _bankOpenedAt;
        private DateTime _nextBankPoll, _nextProbeErrorLog;
        private Dictionary<string, string> _bankItems = new Dictionary<string, string>();
        private Dictionary<string, string> _mainItems = new Dictionary<string, string>();
        public bool Active { get; private set; }
        public string LastFilePath { get; private set; }
        public string Status => Active
            ? $"active for site '{_site}' ({_purpose}); bank open={Inventory.Bank.IsOpen}; " +
              $"{_messages} recognized packets, {_rawBankMessages} other bank-window packets, {_bankChanges} item changes"
            : LastFilePath == null ? "inactive; no trace saved in this session" :
              "inactive; last trace: " + LastFilePath;

        public LogisticsProbe(string pluginDir, ManagerLoot.ManagerLoot loot, Action<string> say)
        {
            _say = say;
            _loot = loot;
            _directory = Path.Combine(pluginDir, "RKMissionData");
        }

        public void Start(string site = null, string purpose = null)
        {
            if (Active) return;
            if (Game.IsZoning || DynelManager.LocalPlayer == null)
            { _say("Start the logistics probe in a stable playfield."); return; }
            _site = string.IsNullOrWhiteSpace(site) ? "unlabeled" : site.Trim();
            _purpose = string.IsNullOrWhiteSpace(purpose) ? "unspecified" : purpose.Trim().ToLowerInvariant();
            _messages = 0;
            _rawBankMessages = _bankChanges = 0;
            _bankWasOpen = _bankBaselineReady = false;
            _nextBankPoll = _nextProbeErrorLog = DateTime.MinValue;
            _bankItems.Clear();
            _mainItems.Clear();
            _observations.Clear();
            _pendingZone = null;
            _lastStable = null;
            _zoneEndedPending = false;
            _pathPlayfield = 0;
            Active = true;
            Network.N3MessageSent += Sent;
            Network.N3MessageReceived += Received;
            Game.TeleportStarted += ZoneStarted;
            Game.TeleportEnded += ZoneEnded;
            Game.OnUpdate += OnUpdate;
            Observation start = Capture("Start", "Manual logistics route started.");
            var player = DynelManager.LocalPlayer;
            var missionTerminal = DynelManager.AllDynels
                .Where(x => x.Identity.Type == IdentityType.MissionTerminal &&
                    Vector3.Distance(x.Position, player.Position) <= 15f)
                .OrderBy(x => Vector3.Distance(x.Position, player.Position))
                .FirstOrDefault();
            if (missionTerminal != null)
            {
                start.MissionTerminalIdentity = missionTerminal.Identity.ToString();
                start.MissionTerminalPosition = new[] { missionTerminal.Position.X,
                    missionTerminal.Position.Y, missionTerminal.Position.Z };
            }
            else start.Details = "No mission terminal within 15 m at probe start; site origin needs manual verification.";
            _observations.Add(start);
            SampleStable();
            ObserveBankState();
            _say($"Logistics probe for site '{_site}', purpose '{_purpose}' active. " +
                (missionTerminal == null ? "No mission terminal was nearby at start. " :
                    $"Mission terminal {missionTerminal.Identity} recorded. ") +
                "Walk the actual route into the bank/shop building or backyard, " +
                "use the terminal, and walk back out; then use /rkm logistics probe stop. " +
                "Playfield changes and terminal positions will be saved with the transaction trace.");
        }

        public void Stop()
        {
            if (!Active) return;
            if (!Game.IsZoning && DynelManager.LocalPlayer != null) ObserveBankState(true);
            Network.N3MessageSent -= Sent;
            Network.N3MessageReceived -= Received;
            Game.TeleportStarted -= ZoneStarted;
            Game.TeleportEnded -= ZoneEnded;
            Game.OnUpdate -= OnUpdate;
            if (_pendingZone != null)
                _pendingZone.Details = "Zoning did not reach a stable destination while probe was active.";
            Record("Stop", "Manual logistics route ended.");
            Active = false;
            try
            {
                Directory.CreateDirectory(_directory);
                string path = Path.Combine(_directory,
                    $"logistics-probe-{FileLabel(_site)}-{FileLabel(_purpose)}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(_observations, Formatting.Indented));
                LastFilePath = path;
                _say($"Logistics probe saved {_observations.Count} observations to {path}; " +
                    $"bank item changes={_bankChanges}, other bank-window packets={_rawBankMessages}.");
            }
            catch (Exception ex) { _say("Logistics probe could not save its route/transaction trace: " + ex.Message); }
        }

        private static string FileLabel(string label)
        {
            string safe = new string(label.ToLowerInvariant()
                .Select(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' ? c : '-')
                .Take(32).ToArray()).Trim('-');
            return safe.Length == 0 ? "site" : safe;
        }

        private void ZoneStarted(object sender, EventArgs args)
        {
            _pendingZone = Capture("ZoneStarted", "Observed zoning start.");
            if (_lastStable != null && DateTime.UtcNow - _lastStable.AtUtc < TimeSpan.FromSeconds(2))
            {
                _pendingZone.Playfield = _lastStable.Playfield;
                _pendingZone.Position = _lastStable.Position;
            }
            _observations.Add(_pendingZone);
        }

        private void ZoneEnded(object sender, EventArgs args)
        {
            // The event can precede a usable player position on the arrival
            // playfield. Wait for the first stable update before recording it.
            _zoneEndedPending = true;
        }

        private void OnUpdate(object sender, float elapsed)
        {
            if (!Active || Game.IsZoning || DynelManager.LocalPlayer == null ||
                (!_zoneEndedPending && DateTime.UtcNow < _nextStableSample)) return;
            _nextStableSample = DateTime.UtcNow.AddMilliseconds(150);
            if (!_zoneEndedPending) { SampleStable(); ObserveBankState(); return; }
            _zoneEndedPending = false;
            Observation arrival = Capture("ZoneEnded", "Observed zoning end.");
            if (_pendingZone != null)
            {
                _pendingZone.DestinationPlayfield = arrival.Playfield;
                _pendingZone.DestinationPosition = arrival.Position;
                _say($"Logistics probe zone: PF {_pendingZone.Playfield} -> PF {arrival.Playfield}; " +
                    "both endpoints recorded for route verification.");
                _pendingZone = null;
            }
            _observations.Add(arrival);
            SampleStable();
            ObserveBankState();
        }

        private Dictionary<string, string> Snapshot(IEnumerable<Item> items, bool classify = false)
        {
            var snapshot = new Dictionary<string, string>();
            int index = 0;
            foreach (Item item in items ?? Enumerable.Empty<Item>())
            {
                if (item != null)
                {
                    // AOSharp reports UniqueIdentity=None for some ordinary items.
                    // The occupied slot still identifies a bank/main inventory change.
                    string key = item.Slot == Identity.None ? $"index:{index}" : item.Slot.ToString();
                    snapshot[key] = $"item='{item.Name}', id={item.Id}, QL={item.QualityLevel}, " +
                        $"unique={item.UniqueIdentity}" +
                        (classify ? $"; ManagerLoot={_loot.Classify(item)}" : "");
                }
                index++;
            }
            return snapshot;
        }

        private void ObserveBankState(bool force = false)
        {
            try { ObserveBankStateCore(force); }
            catch (Exception ex) { ReportProbeError("Bank inventory snapshot", ex); }
        }

        private void ObserveBankStateCore(bool force)
        {
            bool open = Inventory.Bank.IsOpen;
            if (!force && open == _bankWasOpen && DateTime.UtcNow < _nextBankPoll) return;
            _nextBankPoll = DateTime.UtcNow.AddMilliseconds(open ? 250 : 1000);
            Dictionary<string, string> main = Snapshot(Inventory.Items.Where(x =>
                x.Slot.Type == IdentityType.Inventory), true);
            if (open != _bankWasOpen)
            {
                _bankWasOpen = open;
                _bankBaselineReady = false;
                _bankOpenedAt = DateTime.UtcNow;
                Record(open ? "BankOpened" : "BankClosed",
                    $"bank items={Inventory.Bank.Items?.Count ?? 0}; main items={main.Count}; free slots={Inventory.NumFreeSlots}");
                _say(open ? "Logistics probe: bank opened; inventory snapshots are starting." :
                    "Logistics probe: bank closed; item and packet observations are in the saved trace after probe stop.");
            }
            if (!open)
            {
                _bankItems.Clear();
                _mainItems = main;
                return;
            }
            Dictionary<string, string> bank = Snapshot(Inventory.Bank.Items);
            if (!_bankBaselineReady)
            {
                if (DateTime.UtcNow - _bankOpenedAt < TimeSpan.FromSeconds(1)) return;
                _bankItems = bank;
                _mainItems = main;
                _bankBaselineReady = true;
                Record("BankBaseline", $"bank items={bank.Count}; main items={main.Count}; free slots={Inventory.NumFreeSlots}");
                _say($"Logistics probe: bank baseline captured ({bank.Count} stored items, {main.Count} main items). " +
                    "Deposit one item, then retrieve one item before stopping the probe.");
                return;
            }
            RecordChanges("BankItemAdded", bank, _bankItems);
            RecordChanges("BankItemRemoved", _bankItems, bank);
            RecordChanges("MainItemAdded", main, _mainItems);
            RecordChanges("MainItemRemoved", _mainItems, main);
            _bankItems = bank;
            _mainItems = main;
        }

        private void RecordChanges(string eventName, Dictionary<string, string> after,
            Dictionary<string, string> before)
        {
            foreach (var item in after.Where(x => !before.TryGetValue(x.Key, out string previous) ||
                previous != x.Value))
            {
                Record(eventName, $"slot={item.Key}; {item.Value}; free slots={Inventory.NumFreeSlots}");
                _bankChanges++;
                _say($"Logistics probe: {eventName} {item.Key} ({item.Value}).");
            }
        }

        private void SampleStable()
        {
            if (!Game.IsZoning && DynelManager.LocalPlayer != null)
            {
                _lastStable = Capture("StableSample", "Stable playfield and position.", false);
                Vector3 position = DynelManager.LocalPlayer.Position;
                if (_pathPlayfield != _lastStable.Playfield ||
                    Vector3.Distance(position, _lastPathPoint) >= 0.75f)
                {
                    _pathPlayfield = _lastStable.Playfield;
                    _lastPathPoint = position;
                    _observations.Add(new Observation
                    {
                        AtUtc = _lastStable.AtUtc,
                        Event = "PathPoint",
                        Site = _site,
                        Purpose = _purpose,
                        Playfield = _pathPlayfield,
                        Position = _lastStable.Position,
                        NearbyTerminals = Array.Empty<string>()
                    });
                }
            }
        }

        private Observation Capture(string eventName, string details, bool includeTerminals = true)
        {
            var player = DynelManager.LocalPlayer;
            // TeleportStarted may already set IsZoning while the source
            // playfield and player position still identify the departure.
            int playfield = player == null ? 0 : Playfield.ModelIdentity.Instance;
            Vector3 position = player?.Position ?? default(Vector3);
            return new Observation
            {
                AtUtc = DateTime.UtcNow,
                Event = eventName,
                Site = _site,
                Purpose = _purpose,
                Playfield = playfield,
                Position = player == null ? null : new[] { position.X, position.Y, position.Z },
                Details = details,
                NearbyTerminals = !includeTerminals || playfield == 0 ? Array.Empty<string>() : DynelManager.Terminals
                    .Where(x => Vector3.Distance(x.Position, position) <= 8f)
                    .OrderBy(x => Vector3.Distance(x.Position, position))
                    .Take(8)
                    .Select(x => $"{x.Identity} '{x.Name}' ({x.Position.X:0.0},{x.Position.Y:0.0},{x.Position.Z:0.0})")
                    .ToArray()
            };
        }

        private void Record(string eventName, string details) =>
            _observations.Add(Capture(eventName, details));

        private void Sent(object sender, N3Message message) => TryObserve("sent", message);
        private void Received(object sender, N3Message message) => TryObserve("received", message);

        private void TryObserve(string direction, N3Message message)
        {
            try { Observe(direction, message); }
            catch (Exception ex) { ReportProbeError("Packet observation", ex); }
        }

        private void ReportProbeError(string stage, Exception ex)
        {
            if (DateTime.UtcNow < _nextProbeErrorLog) return;
            _nextProbeErrorLog = DateTime.UtcNow.AddSeconds(5);
            _say($"Logistics probe {stage} error: {ex.Message}");
        }

        private string DescribeMainSource(Identity slot)
        {
            Item item = Inventory.Items.FirstOrDefault(x => x.Slot == slot);
            return item == null ? "source item unresolved" :
                $"item='{item.Name}', unique={item.UniqueIdentity}, id={item.Id}, QL={item.QualityLevel}, " +
                $"ManagerLoot={_loot.Classify(item)}";
        }

        private void Observe(string direction, N3Message message)
        {
            string details = null;
            if (message is BankMessage bank)
                details = $"Bank identity={bank.Identity}, slots={bank.BankSlots?.Length ?? 0}; " +
                    "first slots=" + string.Join(",", (bank.BankSlots ?? Array.Empty<SmokeLounge.AOtomation.Messaging.GameData.InventorySlot>())
                        .Take(12).Select(x => $"{x.Placement}:{x.Identity}"));
            else if (message is InventoryUpdateMessage inventory)
                details = $"InventoryUpdate identity={inventory.InventoryIdentity}, handle={inventory.Handle}, " +
                    $"items={inventory.Items?.Length ?? 0}; first slots=" +
                    string.Join(",", (inventory.Items ?? Array.Empty<SmokeLounge.AOtomation.Messaging.GameData.InventorySlot>())
                        .Take(12).Select(x => $"{x.Placement}:{x.Identity}"));
            else if (message is ShopUpdateMessage shop)
                details = $"ShopUpdate slots={shop.VendingMachineSlots?.Length ?? 0}";
            else if (message is VendingMachineFullUpdateMessage vending)
                details = $"VendingMachine owner={vending.OwnerType}:{vending.OwnerInstance}";
            else if (message is ClientContainerAddItem move)
                details = $"MoveToContainer source={move.Source}, target={move.Target}; " +
                    (move.Source.Type == IdentityType.Inventory ? DescribeMainSource(move.Source) :
                        "source outside main inventory");
            else if (message is ContainerAddItem added)
                details = $"ContainerAddItem source={added.Source}, target={added.Target}, slot={added.Slot}";
            else if (message is ClientMoveItemToInventory take)
                details = $"MoveToInventory source={take.SourceContainer}, slot={take.Slot}";
            else if (message is TradeMessage trade)
                details = $"Trade action={trade.Action}, params={trade.Param1}/{trade.Param2}/{trade.Param3}/{trade.Param4}";
            if (details == null)
            {
                if (Inventory.Bank.IsOpen && _rawBankMessages < 160)
                {
                    Record(direction, $"Unclassified bank-window packet: {message.GetType().FullName}; identity={message.Identity}");
                    _rawBankMessages++;
                }
                return;
            }
            Record(direction, $"{details}; bank open={Inventory.Bank.IsOpen}; main free slots={Inventory.NumFreeSlots}");
            Observation observed = _observations[_observations.Count - 1];
            _say($"Logistics probe {direction} in PF {observed.Playfield}: {details}; " +
                $"bank open={Inventory.Bank.IsOpen}, main free slots={Inventory.NumFreeSlots}.");
            if (++_messages >= 200) Stop();
        }

        public void Dispose() => Stop();
    }
}
