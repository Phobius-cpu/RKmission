using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    // Lift discovery follows DungeonSolver's up/down/boss button convention.
    // The room route and doorway crossing remain MissionDungeon's responsibility.
    internal sealed class DungeonLiftController
    {
        internal sealed class Lift
        {
            public Identity Identity;
            public int RoomId;
            public int Floor;
            public Vector3 Position;
        }

        private readonly Dictionary<string, Lift> _known = new Dictionary<string, Lift>();
        private DateTime _lastUse;

        public void Reset()
        {
            _known.Clear();
            _lastUse = DateTime.MinValue;
        }

        public void Discover()
        {
            if (!Playfield.IsDungeon) return;
            foreach (SimpleItem button in DynelManager.Terminals.Where(x => x.Room != null))
            {
                string name = button.Name;
                if (name != "Button (up)" && name != "Button (down)" && name != "Button (boss)")
                    continue;
                int floor = Math.Abs(button.Room.Floor);
                _known[floor + ":" + name] = new Lift
                {
                    Identity = button.Identity, RoomId = button.Room.Instance,
                    Floor = floor, Position = button.Position
                };
            }
        }

        public bool TryGet(int floor, bool forward, out Lift lift)
        {
            string direction = Playfield.DungeonDirection == DungeonDirection.Up
                ? (forward ? "Button (up)" : "Button (down)")
                : (forward ? "Button (down)" : "Button (up)");
            if (forward && _known.TryGetValue(floor + ":Button (boss)", out lift)) return true;
            return _known.TryGetValue(floor + ":" + direction, out lift);
        }

        public void Use(Lift lift)
        {
            if (DateTime.UtcNow - _lastUse < TimeSpan.FromSeconds(2)) return;
            Dynel live = DynelManager.GetDynel(lift.Identity);
            if (live == null) return;
            live.Use();
            _lastUse = DateTime.UtcNow;
        }
    }
}
