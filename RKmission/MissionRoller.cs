using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace RKmission
{
    /// <summary>Rolls until the requested Rubi-Ka playfield appears, then accepts one offer.</summary>
    internal sealed class MissionRoller
    {
        private readonly Action<string> _say;
        private MissionTerminal _terminal;
        private DateTime _nextRequest;
        private int _attempts;
        private Identity _pendingAcceptance = Identity.None;
        private DateTime _acceptedAt;

        public int ZoneId { get; set; }
        public int MaxRolls { get; set; } = 100;
        public byte Difficulty { get; set; } = 128;
        public bool IsRolling { get; private set; }
        public bool HasPendingAcceptance => _pendingAcceptance != Identity.None;
        public Mission Selected { get; private set; }

        public MissionRoller(Action<string> say)
        {
            _say = say;
        }

        public void Start()
        {
            _attempts = 0;
            _pendingAcceptance = Identity.None;
            Selected = null;
            IsRolling = true;
            _nextRequest = DateTime.MinValue;
        }

        public void Stop()
        {
            IsRolling = false;
            _pendingAcceptance = Identity.None;
        }

        public void RememberTerminal(Dynel dynel)
        {
            if (dynel != null && dynel.Identity.Type == IdentityType.MissionTerminal)
                _terminal = new MissionTerminal(dynel);
        }

        public void OnOffers(MissionInfo[] offers)
        {
            if (!IsRolling || _pendingAcceptance != Identity.None || offers == null)
                return;

            // The terminal can be outside the requested zone. In that case the nearest
            // offer is measured from the zone's origin until the player reaches it.
            Vector3 origin = Playfield.ModelIdentity.Instance == ZoneId
                ? DynelManager.LocalPlayer.Position : Vector3.Zero;

            MissionInfo chosen = offers
                .Where(x => x.Playfield.Instance == ZoneId)
                .OrderBy(x => Vector3.Distance(x.Location, origin))
                .FirstOrDefault();

            if (chosen == null)
                return;

            _pendingAcceptance = chosen.MissionIdentity;
            _acceptedAt = DateTime.UtcNow;
            IsRolling = false;
            Network.Send(new CreateQuestMessage { MissionId = _pendingAcceptance });
            _say($"Accepted offer for zone {ZoneId} at {chosen.Location}.");
        }

        public void Tick()
        {
            if (_pendingAcceptance != Identity.None)
            {
                Selected = (Mission.List ?? new System.Collections.Generic.List<Mission>())
                    .FirstOrDefault(x => x.Identity == _pendingAcceptance);
                if (Selected != null)
                {
                    _pendingAcceptance = Identity.None;
                    _say($"Mission accepted: {Selected.DisplayName}.");
                }
                else if (DateTime.UtcNow - _acceptedAt > TimeSpan.FromSeconds(10))
                {
                    _say("Mission acceptance was not confirmed by AO#; stopping.");
                    Stop();
                }
                return;
            }

            if (!IsRolling || DateTime.UtcNow < _nextRequest)
                return;

            if (_attempts >= MaxRolls)
            {
                Stop();
                _say($"No offer in zone {ZoneId} after {MaxRolls} rolls.");
                return;
            }

            if (_terminal == null || !DynelManager.IsValid(_terminal))
            {
                Stop();
                _say("Use a mission terminal, then start RKMission again.");
                return;
            }

            if (Inventory.NumFreeSlots < 2)
            {
                Stop();
                _say("Two free inventory slots are required to roll.");
                return;
            }

            _terminal.RequestMissions(Difficulty);
            _attempts++;
            _nextRequest = DateTime.UtcNow.AddSeconds(2);
        }

        public void SelectAcceptedMission()
        {
            Vector3 origin = DynelManager.LocalPlayer.Position;
            Selected = (Mission.List ?? new System.Collections.Generic.List<Mission>())
                .Where(x => x.Location != null && x.Location.Playfield.Instance == ZoneId)
                .OrderBy(x => Vector3.Distance(x.Location.Pos, origin))
                .FirstOrDefault();
        }
    }
}
