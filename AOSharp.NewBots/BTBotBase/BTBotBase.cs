using AOSharp.Core;
using AOSharp.Core.Misc;
using AOSharp.Core.UI;
using BehaviourTree;
using AOBotBase.BTViewer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Core.IPC;
using BTBotBase;
using AOBotBase.IPCMessages;
using System.Reflection;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;
using BTBotBase.Polls;
using Serilog.Core;

namespace AOBotBase
{
    public class Companion
    {
        public string Name { get; set; }
        public BotRole Role { get; set; }
    }

    public abstract class BTBotBase<TContext> : AOPluginEntry where TContext : IBotContext
    {
        public abstract byte IpcChannelId { get; }

        public bool Enabled = false;
        public BotRole Role = BotRole.Fighter;
        public Identity Leader;
        public bool IsLeader => DynelManager.LocalPlayer.Identity == Leader || Team.IsLeader;
        public Dictionary<Identity, Companion> Roster = new Dictionary<Identity, Companion>();
        public TargetPoll TargetPoll;
        
        protected IPCChannel _ipcChannel;
        protected BTWindow<TContext> _btWindow;
        protected IBehaviour<TContext> _behavior;
        protected TContext _botContext;

        private AutoResetInterval _btTickInterval = new AutoResetInterval(100);

        public override void Run()
        {
            try
            {
                _ipcChannel = new IPCChannel(IpcChannelId);
                _ipcChannel.RegisterCallback((int)BTBotBaseIPCOpcode.BotJoin, CompanionJoined);
                _ipcChannel.RegisterCallback((int)BTBotBaseIPCOpcode.BotLeft, CompanionLeft);
                _ipcChannel.RegisterCallback((int)BTBotBaseIPCOpcode.StartAll, (o, e) => Start());
                _ipcChannel.RegisterCallback((int)BTBotBaseIPCOpcode.RosterUpdate, OnRosterUpdate);

                IPCChannel.LoadMessages(Assembly.GetExecutingAssembly());

                TargetPoll = new TargetPoll(_ipcChannel, Logger);

                Init();

                if (_botContext == null)
                {
                    Logger.Information($"Context must be defined.");
                    return;
                }

                Chat.RegisterCommand("showbt", (string command, string[] param, ChatWindow chatWindow) =>
                {
                    if (_btWindow == null)
                        return;

                    _btWindow.Show();
                    _btWindow.StatusUpdate();
                });

                Chat.RegisterCommand("startall", (string command, string[] param, ChatWindow chatWindow) =>
                {
                    if (Team.IsLeader)
                    {
                        _ipcChannel.Broadcast(new StartAllMessage());
                        Start();
                    }
                    else
                        Logger.Information($"Only the team leader can run this command.");
                });

                Game.OnUpdate += (s, e) => OnUpdate();
                Game.TeleportEnded += (s, e) => OnZoned();
            } 
            catch (Exception ex)
            {
                Logger.Information(ex.ToString());
            }
        }

        public override void Teardown()
        {
            Stop();
        }

        protected virtual void OnUpdate()
        {
            if (Game.IsZoning)
                return;

            if (Enabled && _btTickInterval.Elapsed && DynelManager.LocalPlayer.IsAlive)
            {
                try
                {
                    _behavior.Tick(_botContext);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in behavior tick: {ex}");
                    Logger.Error($"Error in behavior tick: {ex.StackTrace}");
                    Stop();
                    return;
                }
                _btWindow.Update();
            }
        }

        protected virtual void OnZoned()
        {
            _botContext.Reset();
            _behavior?.Reset();
        }

        protected virtual void Init()
        {

        }

        private void OnRosterUpdate(int charId, IPCMessage e)
        {
            RosterUpdateMessage msg = e as RosterUpdateMessage;

            Leader = msg.Leader;
            Roster = msg.Roster.ToDictionary(x => x.Identity, x => new Companion
            {
                Name = x.Name,
                Role = x.Role
            });
        }

        protected virtual void CompanionJoined(int charId, IPCMessage e)
        {
            BotJoinMessage msg = e as BotJoinMessage;

            if (msg.Role != BotRole.Fighter)
                return;

            Identity joiner = new Identity(IdentityType.SimpleChar, charId);
            TargetPoll.RegisterVoter(charId);

            Roster[joiner] = new Companion
            {
                Name = msg.Name,
                Role = msg.Role
            };

            if (Team.IsLeader)
                AnnounceRoster();

            Logger.Debug($"Companion {charId} joined us.");
        }

        protected virtual void CompanionLeft(int charId, IPCMessage e)
        {
            BotLeftMessage msg = e as BotLeftMessage;

            Identity leaver = new Identity(IdentityType.SimpleChar, charId);
            TargetPoll.UnregisterVoter(charId);

            Roster.Remove(leaver);

            if (Team.IsLeader)
                AnnounceRoster();

            Logger.Debug($"Companion {charId} left us.");
        }

        public void StartAll()
        {
            _ipcChannel.Broadcast(new StartAllMessage());
            Start();
        }

        public void StopAll()
        {
            _ipcChannel.Broadcast(new StopAllMessage());
            Stop();
        }

        public virtual void Start()
        {
            if (_behavior == null)
            {
                Logger.Information($"Behavior must be defined prior to calling Start()");
                return;
            }

            _btWindow?.Close();
            _btWindow = new BTWindow<TContext>("", _behavior, $"{PluginDirectory}\\BTViewer\\BTWindow.xml", Logger);

            if (Team.IsLeader)
            {
                Leader = DynelManager.LocalPlayer.Identity;
                Roster[Leader] = new Companion
                {
                    Name = DynelManager.LocalPlayer.Name,
                    Role = BotRole.Fighter
                };
                AnnounceRoster();
            }
            else
            {
                _ipcChannel.Broadcast(new BotJoinMessage
                {
                    Name = DynelManager.LocalPlayer.Name,
                    Role = Role
                });
            }

            _behavior.Reset();
            Enabled = true;
        }

        public virtual void Stop()
        {
            Enabled = false;

            _ipcChannel.Broadcast(new BotLeftMessage());
        }

        private void AnnounceRoster()
        {
            _ipcChannel.Broadcast(new RosterUpdateMessage
            {
                Leader = DynelManager.LocalPlayer.Identity,
                Roster = Roster.Select(x => new RosterUpdateMessage.Companion
                {
                    Identity = x.Key,
                    Name = x.Value.Name,
                    Role = x.Value.Role
                }).ToArray()
            });
        }
    }

    public enum BTBotBaseIPCOpcode
    {
        BotJoin = 12001,
        BotLeft = 12002,
        RosterUpdate = 12003,
        StartAll = 12006,
        StopAll = 12007,

        SharedTarget = 12008,
        PollStart = 12010,
        PollVote = 12011,
        PollClosed = 12012
    }

    public enum BotRole
    {
        Fighter,
        Leecher
    }
}
