using AIMission.Bot.Behaviors;
using AIMission.Bot.IPCMessages;
using AIMission.Bot.Views;
using AOSharp.Common.GameData;
using AOSharp.Core;
using BehaviourTree;
using Dungeon.Runner;
using Dungeon.Solver;
using Serilog;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AIMission.Bot
{
    public enum MissionDifficulty
    {
        Easy,
        Medium,
        Hard
    }

    public class AIMissionBot : DungeonRunner<BTContext>
    {
        internal int MissionsCompleted = 0;
        internal static readonly string RecruiterName = "Unicorn Recruiter";
        internal static readonly string MissionName = "Infiltrate the alien ships!";
        internal static bool HasAlienMission() => Mission.Exists("Infiltrate the alien ships!");

        internal static Config Config;

        private Dictionary<MissionDifficulty, string> difficultySelectionOptions = new Dictionary<MissionDifficulty, string>
        {
            { MissionDifficulty.Easy, "Take it easy on us.  We want to come back in one piece." },
            { MissionDifficulty.Medium, "We can handle our business." },
            { MissionDifficulty.Hard, "I want the mission Unicorns are too chicken to take." },
        };

        private List<string> _ignoredDynels = new List<string>
        {
            "Alien Defense System",
            "Nanovoider"
        };

        public override SolverMode SolverMode => SolverMode.Clear;

        private AIMissionBotWindow _window;

        protected override void Init()
        {
            Config = Config.Load($"{PluginDirectory}\\Config.json");

            _ipcChannel.RegisterCallback((int)AIMissionBotIPCOpcode.SetActiveMission, (e, o) =>
            {
                Logger.Debug("Got mission");
                _botContext.ActiveAIMission = ((SetActiveMissionMessage)o).Identity;

                //Clean up missions
                foreach (Mission mission in Mission.List.Where(x => x.DisplayName == MissionName && x.Identity != _botContext.ActiveAIMission))
                    mission.Delete();
            });

            NpcDialog.AnswerListChanged += NpcDialog_AnswerListChanged;
            Network.N3MessageReceived += N3MessageReceived;
            DynelManager.DynelSpawned += OnDynelSpawned;

            base.Init();
            ReadyPoll.PollOpened += OnReadyPollOpened;

            _window = new AIMissionBotWindow("AI Mission Bot", $"{PluginDirectory}\\Views\\Window.xml", this);
            _window.Show();
        }

        private void OnDynelSpawned(object sender, Dynel e)
        {
            if (!Playfield.IsDungeon)
                return;

            if (!Enabled)
                return;

            if (Config.Bosses.TryGetValue(e.Name, out bool kill))
            {
                if (!kill)
                {
                    _botContext.ExitingDungeon = true;

                    if (Mission.Find(_botContext.ActiveAIMission.Value, out Mission activeMission))
                    {
                        activeMission.Delete();
                        _botContext.ActiveAIMission = null;
                        Logger.Debug($"Deleting Active Mission: {activeMission.DisplayName} ({activeMission.Identity})");
                    }
                    else
                    {
                        Logger.Debug($"Unable to locate active mission.");

                        Logger.Debug($"Missions:");
                        foreach (Mission mission in Mission.List)
                            Logger.Debug($"\t{mission.DisplayName} ({mission.Identity})");

                        foreach (Mission aiMission in Mission.List.Where(x => x.DisplayName == MissionName))
                            aiMission.Delete();
                    }
                }
            }
        }

        //Uncomment for debug logs
        //protected override void OnConfiguringLogger(LoggerConfiguration loggerConfig)
        //{
        //    ChatLogLevel = AOSharp.Core.Logging.LogLevel.Debug;

        //    base.OnConfiguringLogger(loggerConfig);
        //}

        public override void Start()
        {
            base.Start();
            _window.UpdateButton();
        }

        public override void Stop()
        { 
            base.Stop();
            _window.UpdateButton();
        }

        protected override void EnteredDungeon()
        {
            Config.Reload();

            base.EnteredDungeon();
        }

        private void OnReadyPollOpened(PlayfieldId playfieldId)
        {
            if (Playfield.ModelId == playfieldId)
            {
                ReadyPoll.Vote(true);
                return;
            }
        }

        public override bool FindFightableTarget(Room room, out SimpleChar target)
        {
            if (Solver.IsOnBossFloor)
            {
                target = DynelManager.NPCs.Where(c => c.IsAlive && !_ignoredDynels.Contains(c.Name))
                    .OrderByDescending(c => c.Name.StartsWith("Coccoon Attendant") || c.Name.StartsWith("Regeneration Conduit"))
                    .ThenBy(c => Config.Bosses.Keys.Contains(c.Name) || c.Name == "Alien Coccoon")
                    .ThenBy(c => DynelManager.LocalPlayer.DistanceFrom(c))
                    .FirstOrDefault();

                Logger.Debug($"Targets:");
                foreach(var t in DynelManager.NPCs.Where(c => c.IsAlive && !_ignoredDynels.Contains(c.Name))
                    .OrderByDescending(c => c.Name.StartsWith("Coccoon Attendant") || c.Name.StartsWith("Regeneration Conduit"))
                    .ThenBy(c => Config.Bosses.Keys.Contains(c.Name) || c.Name == "Alien Coccoon")
                    .ThenBy(c => DynelManager.LocalPlayer.DistanceFrom(c)))
                {
                    Logger.Debug($"\t{t.Name} - {t.Name.StartsWith("Coccoon Attendant") || t.Name.StartsWith("Regeneration Conduit")} - {Config.Bosses.Keys.Contains(t.Name) || t.Name == "Alien Coccoon"}");
                }

                return target != null;
            }

            return base.FindFightableTarget(room, out target);
        }

        private void N3MessageReceived(object sender, N3Message e)
        {
            if (e.Identity != DynelManager.LocalPlayer.Identity)
                return;

            if (e.N3MessageType != N3MessageType.FormatFeedback)
                return;

            if (((FormatFeedbackMessage)e).Message.Contains("51103:"))
            {
                if (Mission.Find(_botContext.ActiveAIMission.Value, out Mission mission))
                    mission.Delete();
            }
        }

        protected override void OnZoned()
        {
            base.OnZoned();

            if (ReadyPoll.PollOpen)
                return;

            if (Playfield.ModelId == (PlayfieldId)4364)
                ReadyPoll.Vote(true);
        }

        protected override void OnFloorChanged()
        {
            if (Solver.IsOnBossFloor)
            {
                MissionsCompleted++;
                _window.UpdateMissionCounter();
            }

            base.OnFloorChanged();
        }

        protected override void DungeonNavmeshReady()
        {
            if (Team.IsLeader)
            {
                if (!_botContext.ActiveAIMission.HasValue)
                {
                    if (Mission.FindMissionForCurrentDungeon(out Mission mission))
                        _botContext.ActiveAIMission = mission.Identity;
                    else
                    {
                        Logger.Error($"Unable to locate mission for this dungeon.. Stopping.");
                        Stop();
                        return;
                    }
                }

                AnnounceActiveMission(_botContext.ActiveAIMission.Value);
            }

            base.DungeonNavmeshReady();
        }

        protected override IBehaviour<BTContext> PreDungeonTree()
        {
            return RollBehavior.Compile();
        }

        protected override IBehaviour<BTContext> BossRoomTree()
        {
            return IsLeader ? BossBehavior.Leader() : BossBehavior.NonLeader();
        }

        private void NpcDialog_AnswerListChanged(object s, Dictionary<int, string> options)
        {
            if (!DynelManager.Find((Identity)s, out SimpleChar dialogNpc))
                return;

            if (dialogNpc.Name != RecruiterName)
                return;

            foreach (KeyValuePair<int, string> option in options)
            {
                if (option.Value == "I want to visit the alien mothership!" || option.Value == difficultySelectionOptions[_botContext.CurrentDifficulty])
                    NpcDialog.SelectAnswer(dialogNpc.Identity, option.Key);
            }
        }

        public void AnnounceActiveMission(Identity identity)
        {
            _ipcChannel.Broadcast(new SetActiveMissionMessage
            {
                Identity = identity
            });
        }
    }

    public enum AIMissionBotIPCOpcode
    {
        SetActiveMission = 13000,
    }
}
