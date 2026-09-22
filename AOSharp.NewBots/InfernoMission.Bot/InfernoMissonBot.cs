using AIMission.Bot.Views;
using AOBotBase;
using AOBotBase.BTViewer;
using AOBotBase.IPCMessages;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.IPC;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using BehaviourTree;
using BehaviourTree.FluentBuilder;
using BTBotBase.Polls;
using Dungeon.Runner.IPCMessages;
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace InfernoMission.Bot
{
    public enum InfernoMissionBotIPCOpcode
    {
        ContextSync = 12051,
        WaveUpdate = 12052
    }

    public enum MissionDifficulty
    {
        Easy,
        Medium,
        Hard
    }

    public class Wave
    {
        public int[] Spirits;
        public int NumAttackers;
    }

    public class InfernoMissionBot : BTBotBase<BTContext>
    {
        public Config Config;
        public Dictionary<string, List<Wave>> Waves;
        public override byte IpcChannelId => 87;
        private MainWindow _window;

        protected override void Init()
        {
            SMovementController.Set();
            SMovementController.AutoLoadNavmeshes($"{PluginDirectory}\\NavMeshes");

            Config = new Config();
            Waves = JsonConvert.DeserializeObject<Dictionary<string, List<Wave>>>(File.ReadAllText($"{PluginDirectory}\\Waves.json"));

            _botContext = new BTContext(this);
            _behavior = Behaviors.Root();

            Chat.RegisterCommand("test", (string command, string[] param, ChatWindow chatWindow) =>
            {
                if (Targeting.TargetChar == null)
                    return;

                Logger.Debug($"Targeting {Targeting.TargetChar.Name} ({Targeting.TargetChar.Identity.Instance})");
            });

            Chat.RegisterCommand("context", (string command, string[] param, ChatWindow chatWindow) =>
            {
                PrintContext();
            });

            _ipcChannel.RegisterCallback((int)InfernoMissionBotIPCOpcode.ContextSync, (o, e) =>
            {
                ContextSyncMessage contextSyncMessage = (ContextSyncMessage)e;

                _botContext.SegmentKey = contextSyncMessage.Segment;
                _botContext.YuttoId = contextSyncMessage.YuttoId;
                _botContext.Wave = contextSyncMessage.Wave;
                _botContext.KillsThisWave = 0;
                Logger.Debug($"Context updated YuttoId: {contextSyncMessage.YuttoId}, Segment: {contextSyncMessage.Segment}, Wave: {contextSyncMessage.Wave}");
            });

            _ipcChannel.RegisterCallback((int)InfernoMissionBotIPCOpcode.WaveUpdate, (o, e) =>
            {
                WaveUpdateMessage waveUpdateMessage = (WaveUpdateMessage)e;

                if (_botContext.KilledWaveMobs.Contains(waveUpdateMessage.MonsterId))
                    return;

                OnWaveMobKilled(waveUpdateMessage.MonsterId);
                Logger.Debug($"Got wave update for {waveUpdateMessage.MonsterId}");
            });

            base.Init();

            _window = new MainWindow("Inferno Mission Bot", $"{PluginDirectory}\\Views\\Window.xml", this);
            _window.Show();

            DynelManager.CharInPlay += OnCharInPlay;
            DynelManager.DynelSpawned += OnDynelSpawned;
            NpcDialog.AnswerListChanged += NpcDialog_AnswerListChanged;
            Network.N3MessageReceived += OnN3Message;
        }

        protected override void OnConfiguringLogger(LoggerConfiguration loggerConfig)
        {
            ChatLogLevel = AOSharp.Core.Logging.LogLevel.Debug;

            base.OnConfiguringLogger(loggerConfig);
        }

        public override void Start()
        {
            base.Start();
            _botContext.MissionStartTime = Time.NormalTime;
            _window.UpdateButton();
        }

        public override void Stop()
        {
            base.Stop();
            _window.UpdateButton();
        }

        public string GetMissionName(MissionDifficulty difficulty, Side side)
        {
            switch (difficulty)
            {
                case MissionDifficulty.Easy:
                    if (side == Side.Neutral)
                        return "The Purification Ritual - Easy";
                    else
                        return "The Purification Ritual - Ea...";
                case MissionDifficulty.Medium:
                    return "The Purification Ritual - Me...";
                case MissionDifficulty.Hard:
                    return "The Purification Ritual - Ha...";
                default:
                    return "Unknown";
            }
        }

        private void NpcDialog_AnswerListChanged(object s, Dictionary<int, string> options)
        {
            SimpleChar dialogNpc = DynelManager.GetDynel((Identity)s).Cast<SimpleChar>();

            if (dialogNpc.Name == Constants.QuestGiverName)
            {
                foreach (KeyValuePair<int, string> option in options)
                {
                    if (option.Value == "Is there anything I can help you with?" ||
                        (_botContext.MissionSide == Side.Clan && option.Value == "I will defend against the Unredeemed!") ||
                        (_botContext.MissionSide == Side.OmniTek && option.Value == "I will defend against the Redeemed!") ||
                        (_botContext.MissionSide == Side.Neutral && option.Value == "I will defend against the creatures of the brink!") ||
                        (_botContext.MissionDifficulty == MissionDifficulty.Easy && option.Value == "I will deal with only the weakest aversaries") || //Brink missions have a typo
                        (_botContext.MissionDifficulty == MissionDifficulty.Easy && option.Value == "I will deal with only the weakest adversaries") ||
                        (_botContext.MissionDifficulty == MissionDifficulty.Medium && option.Value == "I will challenge these invaders, as long as there aren't too many") ||
                        (_botContext.MissionDifficulty == MissionDifficulty.Hard && option.Value == "I will purge the temple of any and all assailants"))
                        NpcDialog.SelectAnswer(dialogNpc.Identity, option.Key);
                }
            }
            else if (dialogNpc.Name == Constants.QuestStarterName)
            {
                foreach (KeyValuePair<int, string> option in options)
                {
                    if (option.Value == "Yes, I am ready.")
                        NpcDialog.SelectAnswer(dialogNpc.Identity, option.Key);
                }
            }
        }

        private void OnCharInPlay(object sender, SimpleChar e)
        {
            if (!IsLeader)
                return;

            if (!_botContext.IsTeamStale)
                return;

            if (Playfield.ModelId != PlayfieldId.Inferno)
                return;

            if (_botContext.TeamSnapshot == null || !_botContext.TeamSnapshot.Contains(e.Identity))
                return;

            Team.Invite(e);
        }

        private void OnN3Message(object sender, N3Message e)
        {
            if (e is CharacterActionMessage charActionMsg)
            {
                if (charActionMsg.Action != CharacterActionType.Death)
                    return;

                if (!DynelManager.Find(charActionMsg.Identity, out SimpleChar character))
                    return;

                if (_botContext.IsWaveMob(character))
                {
                    Logger.Debug($"Wave mob {character.Name} dead.");
                    OnWaveMobKilled(character.Identity);
                }
            }

            if (e is QuestMessage questMessage)
            {
                Logger.Debug($"QuestMessage received: {questMessage.Action}, Unk1: {questMessage.Unknown1}, Unk2: {questMessage.Unknown2}, {questMessage.Mission}", ChatColor.Green);

                if (questMessage.Action == QuestAction.Delete)
                {
                    double completionTime = Time.NormalTime - _botContext.MissionStartTime;
                    _window.MissionFinished(completionTime);
                    _behavior.Reset();
                    Logger.Debug($"Mission completed in {completionTime:F2} seconds");
                }
            }
        }

        private void OnWaveMobKilled(Identity mobId)
        {
            if (_botContext.KilledWaveMobs.Contains(mobId))
                return;

            _botContext.KilledWaveMobs.Add(mobId);

            _ipcChannel.Broadcast(new WaveUpdateMessage
            {
                MonsterId = mobId
            });

            PrintContext();

            Wave wave = _botContext.GetCurrentWave();
            if (wave == null)
                return;

            if (++_botContext.KillsThisWave >= wave.NumAttackers)
            {
                _botContext.KillsThisWave = 0;
                _botContext.Spirit = 0;
                _botContext.WaveStartTime = Time.NormalTime;

                if (++_botContext.Wave >= _botContext.GetSegmentWaves().Count)
                {
                    _botContext.Segment++;
                    _botContext.SegmentKey = string.Empty;
                    _botContext.Wave = 0;
                    Logger.Debug($"Segment Finished");
                    _behavior.Reset();


                    if (Team.IsLeader)
                        SyncContext();

                    return;
                }

                wave = _botContext.GetCurrentWave();
                Logger.Information($"Starting Wave {_botContext.Wave}. Spirit: [{string.Join(",", wave.Spirits)}], NumAttackers: {wave.NumAttackers}");
            }
        }

        private void OnDynelSpawned(object sender, Dynel e)
        {
            if (e.Identity.Type != IdentityType.SimpleChar)
                return;

            SimpleChar character = e.Cast<SimpleChar>();

            if (character.IsPet || character.IsPlayer)
                return;

            if (!_botContext.IsWaveMob(character))
                return;

            if (_botContext.SegmentKey == string.Empty && Waves.ContainsKey(character.Name))
            {
                _botContext.SegmentKey = character.Name;
                Logger.Information($"Segment set to {character.Name}");
            }
        }

        public void SyncContext()
        {
            _ipcChannel.Broadcast(new ContextSyncMessage
            {
                Segment = _botContext.SegmentKey,
                YuttoId = _botContext.YuttoId,
                Wave = _botContext.Wave
            });
        }

        public void PrintContext()
        {
            Logger.Debug($"YuttoId: {_botContext.YuttoId}, SegmentKey: {_botContext.SegmentKey}, Segment: {_botContext.Segment}, Wave: {_botContext.Wave}, KillsThisWave: {_botContext.KillsThisWave}, Spirit: {_botContext.Spirit}");
        }
    }
}
