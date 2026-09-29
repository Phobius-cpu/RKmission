using AOSharp.Common.GameData;
using AOSharp.Pathfinding;

namespace RKmission
{
    internal enum MovementOwner
    {
        None, OutdoorTravel, WarpTravel, MissionEntrance, DungeonRoom, DoorTransition,
        LiftTransition, CombatPosition, LootApproach, Objective, DungeonExit
    }

    // The only RKMission path to SMovementController. Ownership changes cancel
    // the previous route before a new subsystem can issue a destination.
    internal sealed class MovementArbiter
    {
        public static MovementArbiter Current { get; set; }
        public MovementOwner Owner { get; private set; }

        public void Claim(MovementOwner owner)
        {
            if (owner == MovementOwner.None || Owner == owner) return;
            SMovementController.Halt();
            Owner = owner;
        }

        public void Navigate(MovementOwner owner, Vector3 destination)
        {
            SetNavDestination(owner, destination);
        }

        public bool SetNavDestination(MovementOwner owner, Vector3 destination)
        { Claim(owner); return SMovementController.SetNavDestination(destination); }

        public bool SetDestination(MovementOwner owner, Vector3 destination)
        { Claim(owner); return SMovementController.SetDestination(destination); }

        public void SetMovement(MovementOwner owner, MovementAction action)
        { Claim(owner); SMovementController.SetMovement(action); }

        public void Halt(MovementOwner owner)
        {
            Claim(owner);
            SMovementController.Halt();
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
        }
    }
}
