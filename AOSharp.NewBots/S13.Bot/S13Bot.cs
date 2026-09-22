using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using AOBotBase;
using AOBotBase.BTViewer;
using AOBotBase.IPCMessages;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.Movement;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using BehaviourTree;
using BehaviourTree.FluentBuilder;
using BTBotBase;
using BTBotBase.Polls;
using S13.Bot.Behaviors;
using S13bot.UI;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace S13.Bot
{
    public enum PullStyle
    {
        Safe,
        Aggressive
    }

    public class Settings
    {
        public float MaxPullDistance = 30;
        public float FightDistance = 5;
        public float SafeDistance = 40;
        public float RestedHealthThreshold = 99;
        public float RestedNanoThreshold = 70;
        public float PullWaitDuration = 5;
        public int PreInviteBuffer = 2;
        public int MinAliensForSafePull = 2;
    }

    public class S13Bot : BTBotBase<BotContext>
    {
        public Dictionary<PullStyle, Settings> Settings = new Dictionary<PullStyle, Settings>
        {
            { PullStyle.Safe, new Settings
                {
                    MinAliensForSafePull = 2
                }
            },            
            { PullStyle.Aggressive, new Settings
                {
                    MinAliensForSafePull = 8
                }
            },
        };

        public Settings ActiveSettings => Settings[PullStyle];

        internal PullStyle PullStyle;
        internal int InstancesCompleted = 0;

        public static IEnumerable<SimpleChar> BaseDynelFilter => DynelManager.NPCs.Where(c => !c.IsPet &&
                                                                                         c.IsAlive &&
                                                                                         c.IsInLineOfSight);
        public bool AliensInArea => BaseDynelFilter.Any(c => DynelManager.LocalPlayer.GetLogicalRangeToTarget(c) < ActiveSettings.MaxPullDistance);
        public bool FightableAliens => FindFightableTarget(out _);

        public override byte IpcChannelId => 77;

        public NewNavmeshMovementController NavMovementController;
        public NavmeshLoader NavmeshLoader;

        private S13BotWindow _window;

        protected override void Init()
        {
            try
            {
                NavMovementController = new NewNavmeshMovementController(true);
                MovementController.Set(NavMovementController);

                NavmeshLoader = new NavmeshLoader($"{PluginDirectory}\\Navmeshes");
                NavmeshLoader.NavmeshLoaded += NavmeshLoaded;
                NavmeshLoader.LoadFailed += NavmeshLoadFailed;
                NavmeshLoader.LoadCurrentPlayfield();

                _botContext = new BotContext(this);
                _behavior = BaseBehavior.Compile();

                TargetPoll.PollResult += OnTargetPollResult;
                TargetPoll.PollOpened += OnTargetPollOpened;

                _ipcChannel.RegisterCallback((int)BTBotBaseIPCOpcode.SharedTarget, (o, e) =>
                {
                    Logger.Information($"Recieved Target: {((SharedTargetMessage)e).Target}");
                    _botContext.TargetId = ((SharedTargetMessage)e).Target;
                });

                Chat.RegisterCommand("starts13", (string command, string[] param, ChatWindow chatWindow) => Start());
                Chat.RegisterCommand("stops13", (string command, string[] param, ChatWindow chatWindow) => Stop());
                Chat.RegisterCommand("testpull", (string command, string[] param, ChatWindow chatWindow) =>
                {
                    if (Targeting.TargetChar == null)
                        return;

                    if (Inventory.Find("Aggression Enhancer", out Item aggTool))
                        aggTool.Use();
                });

                Chat.RegisterCommand("testreform", (string command, string[] param, ChatWindow chatWindow) =>
                {
                    _botContext.NeedsToReform = true;
                });

                _window = new S13BotWindow("AI Mission Bot", $"{PluginDirectory}\\UI\\Views\\Window.xml", this);
                _window.Show();

                Logger.Information("S13 Bot Loaded.");
                Logger.Information("Note: You must start this bot from APF Hub or within Sector 13 itself.");
                DynelManager.CharInPlay += OnCharInPlay;
                Network.N3MessageReceived += OnN3Message;
                Team.TeamRequest += OnTeamRequest;
            }
            catch (Exception ex)
            {
                Logger.Information(ex.ToString());
            }
        }

        private void OnTeamRequest(object sender, TeamRequestEventArgs e)
        {
            if (e.Requester == Leader)
                e.Accept();
        }

        protected override void OnZoned()
        {
            _window.UpdateInstanceCounter();

            Logger.Information("Team Snapshot:");
            foreach (TeamMember member in Team.Members)
                Logger.Information($"\t{member.Name}");

            _botContext.Reset();

            if (Playfield.ModelId == PlayfieldId.Sector13)
            {
                foreach (Identity teamMember in _botContext.TeamSnapshot.Except(Team.Members.Select(x => x.Identity)))
                    Team.Invite(teamMember);

                _botContext.TeamSnapshot = null;
            }

            base.OnZoned();
        }

        private void OnN3Message(object sender, N3Message msg)
        {
            //Fix for sit bug caused by race condiiton on zoning.
            if (msg is FormatFeedbackMessage formatFeedbackMsg)
            {
                if (formatFeedbackMsg.FormattedMessage == "~&!!!\":%a(/O")
                    MovementController.Instance.SetMovement(MovementAction.LeaveSit);
            }
        }

        private void OnCharInPlay(object sender, SimpleChar e)
        {
            if (!Team.IsLeader)
                return;

            if (!_botContext.NeedsToReform)
                return;

            if (Playfield.ModelId != PlayfieldId.APFHub)
                return;

            if (!_botContext.TeamSnapshot.Contains(e.Identity))
                return;

            Team.Invite(e);
        }

        private void OnTargetPollOpened(PlayfieldId playfieldId)
        {
            if (Playfield.ModelId != playfieldId)
            {
                TargetPoll.Vote(null);
                return;
            }

            bool foundTarget = FindFightableTarget(out SimpleChar target);

            Logger.Debug($"Voting for target: {(foundTarget ? target.Name : "null")}");
            TargetPoll.Vote(target);
        }

        private void OnTargetPollResult(List<PollResult<TargetPollAnswer>> list, bool timedOut)
        {
            _botContext.PollingEnded?.Set();

            var winner = list.First().Answer.Target;

            _botContext.TargetId = winner;

            Logger.Information($"We've decided on target {winner}");

            _ipcChannel.Broadcast(new SharedTargetMessage
            {
                Target = winner
            });
        }

        protected override void OnUpdate()
        {
            PullStyle = _window.GetSelectedPullStyle();

            if (Playfield.ModelId == PlayfieldId.APFHub && Team.IsInTeam && !Team.IsRaid)
                Team.ConvertToRaid();

            DebugDrawMobPathing();

            base.OnUpdate();
        }

        public override void Start()
        {
            base.Start();
            _window.UpdateButton();
        }

        public override void Stop()
        {
            base.Stop();
            MovementController.Instance.Halt();
            _window?.UpdateButton();
        }

        public void DebugDrawMobPathing()
        {
            foreach (SimpleChar character in DynelManager.Characters)
            {
                if (character.IsPathing)
                {
                    Vector3 rayOrigin = character.PathingDestination;
                    rayOrigin.Y += 5;
                    Vector3 rayTarget = rayOrigin;
                    rayTarget.Y = 0;

                    if (Playfield.Raycast(rayOrigin, rayTarget, out Vector3 hitPos, out _))
                    {
                        Debug.DrawLine(rayOrigin, rayTarget, DebuggingColor.White);
                        Debug.DrawSphere(hitPos, 0.2f, DebuggingColor.White);
                    }

                    Debug.DrawLine(character.Position, character.PathingDestination, DebuggingColor.LightBlue);
                    Debug.DrawSphere(character.PathingDestination, 0.2f, DebuggingColor.LightBlue);
                }
                if (character.IsInLineOfSight)
                    Debug.DrawLine(DynelManager.LocalPlayer.Position, character.Position, DebuggingColor.Green);
            }
        }

        public bool FindPullTarget(out SimpleChar target)
        {
            target = BaseDynelFilter
                            .Where(c => DynelManager.LocalPlayer.GetLogicalRangeToTarget(c) < ActiveSettings.MaxPullDistance)
                            .OrderBy(c => c.Position.DistanceFrom(DynelManager.LocalPlayer.Position))
                            .FirstOrDefault();

            return target != null;
        }

        public bool FindFightableTarget(out SimpleChar target)
        {
            target = BaseDynelFilter
                            .Where(c => IsSafe(c) || (_botContext.PullTargetId.HasValue ? DynelManager.LocalPlayer.GetLogicalRangeToTarget(c) < ActiveSettings.FightDistance : c.FightingTarget != null) || (c.Identity == _botContext.PullTargetId && Time.NormalTime > _botContext.FightStartTime + ActiveSettings.PullWaitDuration))
                            .OrderByDescending(c => c.IsInAttackRange(true))
                            .ThenByDescending(c => c.FightingTarget != null)
                            .ThenByDescending(c => c.Name.Contains("Ilari"))
                            .ThenBy(c => c.Health)
                            .FirstOrDefault();

            return target != null;
        }

        public bool IsSafe(SimpleChar target)
        {
            return CountMobsCloserToMe(target) <= ActiveSettings.MinAliensForSafePull && DynelManager.NPCs.Count(c => !c.IsPet && c.IsAlive && c.DistanceFrom(target) < ActiveSettings.SafeDistance) <= ActiveSettings.MinAliensForSafePull;
        }

        public int CountMobsCloserToMe(SimpleChar target)
        {
            float targetDist = target.DistanceFrom(DynelManager.LocalPlayer);
            return DynelManager.NPCs.Count(c => !c.IsPet && c.IsAlive && c.DistanceFrom(DynelManager.LocalPlayer) < targetDist);
        }

        public bool IsRested()
        {
            return DynelManager.LocalPlayer.GetStat(Stat.TemporarySkillReduction) == 0 &&
                   DynelManager.LocalPlayer.HealthPercent > ActiveSettings.RestedHealthThreshold &&
                   DynelManager.LocalPlayer.NanoPercent > ActiveSettings.RestedNanoThreshold;
        }

        private void NavmeshLoadFailed(NavmeshLoader.LoadFailedEventArgs e)
        {
            Stop();

            if (e.Reason == NavmeshLoader.LoadFailedReason.InvalidMovementController)
                Logger.Information($"Invalid MovementController Type. NavmeshLoader requires a MovementController of type NewNavmeshMovementController");
            else if (e.Reason == NavmeshLoader.LoadFailedReason.NotFound)
                Logger.Information($"Unable to find Navmesh for playfield {e.PlayfieldId} ({(int)e.PlayfieldId})");
            else if (e.Reason == NavmeshLoader.LoadFailedReason.Unknown)
                Logger.Information($"Failed to load Navmesh for playfield {e.PlayfieldId} ({(int)e.PlayfieldId})");
        }

        private void NavmeshLoaded(PlayfieldId playfieldId)
        {
            Logger.Information($"Navmesh loaded for playfield {playfieldId} ({(int)playfieldId})");
        }
    }
}
