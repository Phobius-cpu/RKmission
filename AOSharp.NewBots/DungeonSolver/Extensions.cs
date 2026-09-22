using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Dungeon.Solver
{
    public static class Extensions
    {
        public static bool IsClear(this Room room, List<int> visitedRooms, SolverMode mode)
        {
            if (room.IsEmpty() || mode == SolverMode.Blitz)
            {
                if (!room.GetUnvisitedDoors(visitedRooms).Any())
                    return true;

                if (room.NumDoors == 1 && room.Instance == DynelManager.LocalPlayer.Room.Instance)
                    return true;
            }

            return false;
        }

        public static bool IsEmpty(this Room room)
        {
            return !DynelManager.NPCs.Any(x => !x.IsPet && x.IsAlive && x.Room.Instance == room.Instance);
        }

        public static bool IsMainHall(this Room room)
        {
            return DungeonSolver.MainHallNames.Any(x => room.Name.StartsWith(x));
        }

        public static bool IsEndblocker(this Room room)
        {
            return room.Name.Contains("endblock") || room.Name.Contains("miniblocker");
        }

        public static List<int> GetUnvisitedDoors(this Room room, List<int> visitedRooms)
        {
            List<int> unvisitedDoors = new List<int>();
            for (int i = 0; i < room.NumDoors; i++)
            {
                int idx = room.GetDoorConnectZone(i);
                if (idx == -1 || visitedRooms.Contains(idx))
                    continue;

                unvisitedDoors.Add(i);
            }

            return unvisitedDoors;
        }

        public static bool DoorwayLeadsToLift(this Room room, int doorIdx, Lift lift)
        {
            return room.GetPathTo(lift.Room).Contains(room.GetDoorConnectZone(doorIdx));
        }

        public static List<int> PathToLift(this Room room, Lift lift)
        {
            return room.GetPathTo(lift.Room);
        }

        public static List<int> GetPathTo(this Room room1, Room room2)
        {
            List<int> path = new List<int>();
            List<int> visited = new List<int>();
            Queue<int> queue = new Queue<int>();

            visited.Add(room1.Instance);
            queue.Enqueue(room1.Instance);

            while (queue.Any())
            {
                int inst = queue.Dequeue();
                path.Add(inst);

                if (inst == room2.Instance)
                    break;

                for (int i = 0; i < Playfield.Rooms[inst].NumDoors; i++)
                {
                    int dstInst = Playfield.Rooms[inst].GetDoorConnectZone(i);

                    if (dstInst == -1) 
                        continue;

                    if (visited.Contains(dstInst))
                        continue;

                    visited.Add(dstInst);
                    queue.Enqueue(dstInst);
                }
            }

            path.Reverse();

            List<int> pathLinks = new List<int>();
            int lastValidId = room2.Instance;

            foreach (int idx in path)
            {
                for (int i = 0; i < Playfield.Rooms[idx].NumDoors; i++)
                {
                    if (Playfield.Rooms[idx].GetDoorConnectZone(i) == lastValidId)
                    {
                        pathLinks.Add(idx);
                        lastValidId = idx;
                    }
                }
            }

            pathLinks.Reverse();
            pathLinks.Add(room2.Instance);

            return pathLinks;
        }
    }
}
