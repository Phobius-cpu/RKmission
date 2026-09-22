using AOBotBase;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using BehaviourTree.FluentBuilder;
using BehaviourTree;
using Dungeon.Runner.Behaviors;
using Dungeon.Solver;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using Dungeon.Runner.IPCMessages;
using AOSharp.Core.IPC;
using System.Reflection;
using AOBotBase.IPCMessages;
using BTBotBase.Polls;

namespace Dungeon.Runner
{
    public abstract class DungeonRunner<TContext> : BTBotBase<TContext> where TContext : DungeonRunnerContext<TContext>
    {
        public const float FightDistance = 10;

        public DungeonSolver Solver;
        public abstract SolverMode SolverMode { get; }

        public ReadyPoll ReadyPoll;
        private IEnumerable<SimpleChar> BaseDynelFilter => DynelManager.NPCs.Where(c => !c.IsPet && c.IsAlive);

        public override byte IpcChannelId => 88;

        protected override void Init()
        {
            SMovementController.Set();
            SMovementController.AutoLoadNavmeshes($"{PluginDirectory}\\NavMeshes");
            SMovementController.OnRubberband += (_) => _botContext.IsPathStale = true;

            _botContext = (TContext)Activator.CreateInstance(typeof(TContext), new object[] { this });

            TargetPoll.PollResult += OnTargetPollResult;
            TargetPoll.PollOpened += OnTargetPollOpened;

            ReadyPoll = new ReadyPoll(_ipcChannel, Logger);
            ReadyPoll.PollResult += OnReadyPollResult;

            if (Playfield.IsDungeon)
                InitSolver();

            Chat.RegisterCommand("dumpclear", (string command, string[] param, ChatWindow chatWindow) =>
            {
                if (!Playfield.IsDungeon)
                    return;

                foreach(Room room in Playfield.Rooms)
                {
                    if (room.Floor != DynelManager.LocalPlayer.Room.Floor)
                        continue;

                    Chat.WriteLine($"{room.Name} ({room.Instance}) IsEmpty: {room.IsEmpty()}");
                }
            });

            Chat.RegisterCommand("dumpdungeon", (string command, string[] param, ChatWindow chatWindow) =>
            {
                if (!Playfield.IsDungeon)
                    return;

                foreach (Room room in Playfield.Rooms)
                {
                    Chat.WriteLine($"{room.Name} ({room.Instance})");
                }
            });

            IPCChannel.LoadMessages(Assembly.GetExecutingAssembly());

            _ipcChannel.RegisterCallback((int)DungeonRunnerIPCOpcode.LeaderMovement, (o, e) =>
            {
                LeaderMovementMessage leaderMovementMessage = (LeaderMovementMessage)e;

                Logger.Debug($"LeaderMovement - Floor: {leaderMovementMessage.Floor} Destination: {leaderMovementMessage.Destination} (Leader: {o})");

                if (leaderMovementMessage.Destination == Vector3.Zero && leaderMovementMessage.Floor == 0)
                {
                    _botContext.ExitingDungeon = true;
                }
                else
                {
                    _botContext.LeaderMovement = new LeaderMovement
                    {
                        Floor = leaderMovementMessage.Floor,
                        Destination = leaderMovementMessage.Destination
                    };
                }
                _botContext.IsPathStale = true;
                _behavior.Reset();
            });

            _ipcChannel.RegisterCallback((int)BTBotBaseIPCOpcode.SharedTarget, (o, e) =>
            {
                Logger.Debug($"Received Target: {((SharedTargetMessage)e).Target}");
                _botContext.TargetId = ((SharedTargetMessage)e).Target;
            });

            SMovementController.DestinationReached += (_) => _botContext.IsPathStale = true;

            Network.N3MessageReceived += OnN3Message;
            Logger.Information("DungeonRunner Loaded");
        }

        public override void Start()
        {
            if (!Team.IsLeader)
                ReadyPoll.Vote(true);

            _behavior = CreateBT();

            base.Start();
        }

        public override void Stop()
        {
            SMovementController.Halt();

            base.Stop();
        }

        protected virtual void OnFloorChanged()
        {
            _botContext.IsPathStale = true;
            _botContext.FloorChanged?.Set();
        }

        protected virtual void EnteredDungeon()
        {
            if (!Team.IsLeader)
                ReadyPoll.Vote(true);

            _botContext.ActiveMission = Mission.List.FirstOrDefault(x => x.PlayfieldInstance == Playfield.ModelIdentity);
        }

        protected virtual void DungeonNavmeshReady()
        {

        }

        public bool FindFightableTarget(out SimpleChar target)
        {
            return FindFightableTarget(null, out target);
        }

        public virtual bool FindFightableTarget(Room room, out SimpleChar target)
        {
            target = BaseDynelFilter
                            .Where(c => (room != null && c.Room.Instance == room.Instance) || c.FightingTarget != null || (DynelManager.LocalPlayer.DistanceFrom(c) < FightDistance && c.IsInLineOfSight))
                            .OrderBy(c => DynelManager.LocalPlayer.DistanceFrom(c))
                            .FirstOrDefault();

            return target != null;
        }

        private void InitSolver()
        {
            //Solver = new DungeonSolver(Team.IsLeader ? SolverMode : SolverMode.Blitz, Logger);
            Solver = new DungeonSolver(SolverMode, Logger);
            Solver.LoadNavmesh();
            Solver.SetInitialRoom(DynelManager.LocalPlayer.Room);
            Solver.FloorChanged += OnFloorChanged;
            Solver.NavMeshLoadFailed += () => Logger.Information($"Navmesh load failed.");
            Solver.NavMeshLoadFinished += DungeonNavmeshReady;
            EnteredDungeon();
        }

        private void OnTargetPollOpened(PlayfieldId playfieldId)
        {
            if (Playfield.ModelId != playfieldId || Game.IsZoning)
            {
                TargetPoll.Vote(null);
                return;
            }

            FindFightableTarget(out SimpleChar target);

            TargetPoll.Vote(target);

            Logger.Debug($"Voting on target {target?.Identity.Instance.ToString("X")} ({target.Name})");
        }

        private void OnTargetPollResult(List<PollResult<TargetPollAnswer>> list, bool timedOut)
        {
            _botContext.TargetPollEnded?.Set();

            var winner = list.First().Answer.Target;

            _botContext.TargetId = winner;

            Logger.Debug($"We've decided on target {winner}");

            _ipcChannel.Broadcast(new SharedTargetMessage
            {
                Target = winner
            });
        }

        private void OnReadyPollResult(List<PollResult<ReadyPollAnswer>> results, bool timedOut)
        {
            Logger.Debug($"OnReadyPollResult - TimedOut: {timedOut}");
            _botContext.TeamReady = !timedOut;

            if (!_botContext.TeamReady)
            {
                Logger.Error($"Some bots didn't ready up.. Stopping");
                Stop();
                return;
            }

            Logger.Debug($"OnReadyPollResult - Set");
            _botContext.ReadyPollEnded.Set();
        }

        public void AnnounceLeaderMovement(Vector3 destination)
        {
            _ipcChannel.Broadcast(new LeaderMovementMessage
            {
                Floor = DynelManager.LocalPlayer.Room.Floor,
                Destination = destination
            });
        }

        public void AnnounceGoingToLift()
        {
            _ipcChannel.Broadcast(new LeaderMovementMessage
            {
                Floor = DynelManager.LocalPlayer.Room.Floor + 1,
                Destination = Vector3.Zero
            });
        }

        public void AnnounceExitingDungeon()
        {
            _ipcChannel.Broadcast(new LeaderMovementMessage
            {
                Floor = 0,
                Destination = Vector3.Zero
            });
        }

        protected override void OnZoned()
        {
            if (!Playfield.IsDungeon)
            {
                Solver?.Dispose();
                Solver = null;
            }
            else
            {
                InitSolver();
            }

            base.OnZoned();
        }

        private void OnN3Message(object sender, N3Message msg)
        {
            if (msg.Identity != DynelManager.LocalPlayer.Identity)
                return;

            if (msg is N3TeleportMessage)
                _botContext.IsPathStale = true;
        }

        protected override void OnUpdate()
        {
            Solver?.Update();

            base.OnUpdate();
        }

        protected override void CompanionJoined(int charId, IPCMessage e)
        {
            base.CompanionJoined(charId, e);

            ReadyPoll.RegisterVoter(charId);
        }

        private IBehaviour<TContext> CreateBT()
        {
            return FluentBuilder.Create<TContext>()
                .Sequence("Root")
                    .Subtree(PreDungeonTree())
                    .Subtree(DungeonRunnerBehavior<TContext>.Compile(BossRoomTree))
                    .Subtree(PostDungeonTree())
                .End()
                .Build();
        }

        protected virtual IBehaviour<TContext> PreDungeonTree()
        {
            return FluentBuilder.Create<TContext>()
                .Sequence("Pre Dungeon")
                    .Do("Blank", (c) => BehaviourStatus.Succeeded)
                .End()
                .Build();
        }

        protected virtual IBehaviour<TContext> BossRoomTree()
        {
            return FluentBuilder.Create<TContext>()
                .Sequence("BossRoom")
                    .Do("Idle", DungeonRunnerBehavior<TContext>.Idle)
                .End()
                .Build();
        }

        protected virtual IBehaviour<TContext> PostDungeonTree()
        {
            return FluentBuilder.Create<TContext>()
                .Sequence("Post Dungeon")
                    .Do("Blank", (c) => BehaviourStatus.Succeeded)
                .End()
                .Build();
        }
    }

    public enum DungeonRunnerIPCOpcode
    {
        LeaderMovement = 12051
    }
}
