using AOSharp.Common.GameData;
using AOSharp.Core.Movement;
using AOSharp.Core.UI;
using AOSharp.Core;
using AOSharp.Pathfinding;
using BehaviourTree;
using org.critterai.nav;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace BTBotBase
{
    public class NavmeshLoader
    {
        public string NavmeshFolderPath;
        public Dictionary<PlayfieldId, Navmesh> NavmeshCache = new Dictionary<PlayfieldId, Navmesh>();

        public event Action<LoadFailedEventArgs> LoadFailed;
        public event Action<PlayfieldId> NavmeshLoaded;

        public NavmeshLoader(string navmeshFolderPath)
        {
            Game.TeleportEnded += OnTeleportEnded;
            NavmeshFolderPath = navmeshFolderPath;
        }

        private void OnTeleportEnded(object sender, EventArgs e)
        {
            LoadCurrentPlayfield();
        }

        public void LoadCurrentPlayfield()
        {
            if (!(MovementController.Instance is NewNavmeshMovementController movementController))
            {
                LoadFailed?.Invoke(new LoadFailedEventArgs(Playfield.ModelId, LoadFailedReason.InvalidMovementController));
                return;
            }

            if (!NavmeshCache.TryGetValue(Playfield.ModelId, out Navmesh navmesh))
            {
                string navMeshPath = $"{NavmeshFolderPath}\\{(int)Playfield.ModelId}.Navmesh";

                if (!File.Exists(navMeshPath))
                {
                    LoadFailed?.Invoke(new LoadFailedEventArgs(Playfield.ModelId, LoadFailedReason.NotFound));
                    return;
                }

                if (!movementController.LoadNavmesh(navMeshPath, out navmesh))
                {
                    LoadFailed?.Invoke(new LoadFailedEventArgs(Playfield.ModelId, LoadFailedReason.Unknown));
                    return;
                }

                NavmeshCache.Add(Playfield.ModelId, navmesh);
            }

            if (movementController.Pathfinder == null || !movementController.Pathfinder.IsUsingNavmesh(navmesh))
            {
                movementController.SetNavmesh(navmesh);
                NavmeshLoaded?.Invoke(Playfield.ModelId);
            }
        }

        public class LoadFailedEventArgs : EventArgs
        {
            public PlayfieldId PlayfieldId;
            public LoadFailedReason Reason;

            public LoadFailedEventArgs(PlayfieldId playfieldId, LoadFailedReason reason)
            {
                PlayfieldId = playfieldId;
                Reason = reason;
            }
        }

        public enum LoadFailedReason
        {
            InvalidMovementController,
            NotFound,
            Unknown
        }
    }
}
