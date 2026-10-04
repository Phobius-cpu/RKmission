using System;
using AOSharp.Common.GameData;
using AOSharp.Pathfinding;

namespace RKmission
{
    internal enum MovementOwner
    {
        None, OutdoorTravel, WarpTravel, FGridTravel, MissionEntrance, DungeonRoom, DoorTransition,
        LiftTransition, CombatPosition, LootApproach, Objective, DungeonExit, Recovery, LogisticsTravel
    }

    // The only RKMission path to SMovementController. Ownership changes cancel
    // the previous route before a new subsystem can issue a destination.
    internal sealed class MovementArbiter
    {
        public static MovementArbiter Current { get; set; }
        public MovementOwner Owner { get; private set; }
        private DateTime _lastDisplacement;
        private int _displacements;

        public int ObserveDisplacement()
        {
            if (Owner == MovementOwner.None) return 0;
            if (DateTime.UtcNow - _lastDisplacement > TimeSpan.FromSeconds(30)) _displacements = 0;
            _lastDisplacement = DateTime.UtcNow;
            return Math.Min(3, ++_displacements);
        }

        public bool Claim(MovementOwner owner)
        {
            if (owner == MovementOwner.None) return false;
            if (Owner == owner) return true;
            // An active doorway crossing is the only owner allowed to end its
            // route. Reclaiming movement mid-crossing can strand the character.
            if (Owner == MovementOwner.DoorTransition) return false;
            SMovementController.Halt();
            Owner = owner;
            return true;
        }

        public void Navigate(MovementOwner owner, Vector3 destination)
        {
            SetNavDestination(owner, destination);
        }

        public bool SetNavDestination(MovementOwner owner, Vector3 destination)
        { return Claim(owner) && SMovementController.SetNavDestination(destination); }

        public bool SetDestination(MovementOwner owner, Vector3 destination)
        { return Claim(owner) && SMovementController.SetDestination(destination); }

        public void SetMovement(MovementOwner owner, MovementAction action)
        { if (Claim(owner)) SMovementController.SetMovement(action); }

        public void Halt(MovementOwner owner)
        {
            if (Claim(owner)) SMovementController.Halt();
        }

        public void Release(MovementOwner owner)
        {
            if (Owner != owner) return;
            SMovementController.Halt();
            Owner = MovementOwner.None;
        }

        public void StopAll()
        {
            SMovementController.Halt();
            Owner = MovementOwner.None;
            _displacements = 0;
        }
    }
}
