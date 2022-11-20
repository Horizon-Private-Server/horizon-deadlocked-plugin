using DotNetty.Common.Internal.Logging;
using Horizon.Plugin.Deadlocked.Messages;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using Server.Plugins.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public class Plugin : IPlugin
    {
        public static string WorkingDirectory = null;
        public static IPluginHost Host = null;
        public static readonly int[] SupportedAppIds = { 11184 };

        private static bool hasQueriedAppSettings = false;
        private static readonly AppSettings defaultAppSettings = new AppSettings(0);
        private static Dictionary<int, AppSettings> appSettingsByAppId = new Dictionary<int, AppSettings>();

        public static AppSettings GetAppSettingsOrDefault(int appId)
        {
            if (appSettingsByAppId.TryGetValue(appId, out var settings))
                return settings;

            return defaultAppSettings;
        }

        public Task Start(string workingDirectory, IPluginHost host)
        {
            WorkingDirectory = workingDirectory;
            Host = host;


            //
            host.RegisterAction(PluginEvent.TICK, OnTick);
            host.RegisterAction(PluginEvent.MEDIUS_PLAYER_ON_GET_POLICY, OnPlayerLoggedIn);
            host.RegisterAction(PluginEvent.MEDIUS_PLAYER_ON_LOGGED_OUT, OnPlayerLoggedOut);
            host.RegisterAction(PluginEvent.MEDIUS_GAME_ON_CREATED, OnGameCreated);
            host.RegisterAction(PluginEvent.MEDIUS_GAME_ON_DESTROYED, OnGameDestroyed);
            host.RegisterAction(PluginEvent.MEDIUS_GAME_ON_STARTED, OnGameStarted);
            host.RegisterAction(PluginEvent.MEDIUS_GAME_ON_ENDED, OnGameEnded);
            host.RegisterAction(PluginEvent.MEDIUS_PLAYER_ON_JOINED_GAME, OnPlayerJoinedGame);
            host.RegisterAction(PluginEvent.MEDIUS_PLAYER_ON_CHAT_MESSAGE, OnPlayerChatMessage);
            host.RegisterAction(PluginEvent.MEDIUS_GAME_ON_HOST_LEFT, OnHostLeftGame);
            host.RegisterAction(PluginEvent.MEDIUS_PLAYER_POST_WIDE_STATS, OnPlayerPostWideStats);
            host.RegisterMediusMessageAction(NetMessageTypes.MessageClassDME, 7, OnRecvCustomMessage);
            host.RegisterMediusMessageAction(NetMessageTypes.MessageClassLobby, (byte)MediusLobbyMessageIds.UpdateClanStats, OnRecvUpdateClanStats);
            host.RegisterMessageAction(RT_MSG_TYPE.RT_MSG_SERVER_CHEAT_QUERY, OnRecvCheatQuery);

            return Task.CompletedTask;
        }

        async Task OnTick(PluginEvent eventId, object data)
        {
            // try and get app settings
            if (!hasQueriedAppSettings && await Server.Medius.Program.Database.AmIAuthenticated() && !String.IsNullOrEmpty(Server.Medius.Program.Database.GetUsername()))
            {
                foreach (var supportedAppId in SupportedAppIds)
                {
                    var appSettings = await Server.Medius.Program.Database.GetServerSettings(supportedAppId);
                    if (!appSettingsByAppId.TryGetValue(supportedAppId, out var settings))
                        appSettingsByAppId.Add(supportedAppId, settings = new AppSettings(supportedAppId));

                    settings.SetSettings(appSettings);
                    
                    // send fully parsed settings to server
                    await Server.Medius.Program.Database.SetServerSettings(supportedAppId, settings.GetSettings());
                }
                
                hasQueriedAppSettings = true;
            }

            await Queue.Tick();
        }

        async Task OnPlayerLoggedIn(PluginEvent eventId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnPlayerRequestArgs)data;
            if (msg.Player == null)
                return;
            if (!SupportedAppIds.Contains(msg.Player.ApplicationId))
                return;

            await Downloader.OnPlayerLoggedIn(msg.Player);
            await Patch.QueryForPatch(msg.Player);
            await Queue.OnPlayerLoggedIn(msg.Player);
        }

        async Task OnPlayerLoggedOut(PluginEvent eventId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnPlayerArgs)data;
            if (msg.Player == null)
                return;
            if (!SupportedAppIds.Contains(msg.Player.ApplicationId))
                return;

            await Downloader.OnPlayerLoggedOut(msg.Player);
            await Player.OnPlayerLoggedOut(msg.Player);
            await Queue.OnPlayerLoggedOut(msg.Player);
        }

        Task OnPlayerChatMessage(PluginEvent eventId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnPlayerChatMessageArgs)data;
            if (msg.Player == null || msg.Player.CurrentChannel == null)
                return Task.CompletedTask;
            if (!SupportedAppIds.Contains(msg.Player.ApplicationId))
                return Task.CompletedTask;

            return Chat.OnChatMessage(msg.Player, msg.Message);
        }

        Task OnGameStarted(PluginEvent eventId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnGameArgs)data;
            if (msg.Game == null)
                return Task.CompletedTask;
            if (!SupportedAppIds.Contains(msg.Game.ApplicationId))
                return Task.CompletedTask;

            return Game.OnGameStarted(msg.Game);
        }

        Task OnGameEnded(PluginEvent eventId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnGameArgs)data;
            if (msg.Game == null)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, $"OnGameEnd with no game");
                return Task.CompletedTask;
            }
            if (!SupportedAppIds.Contains(msg.Game.ApplicationId))
                return Task.CompletedTask;

            return Game.OnGameEnded(msg.Game);
        }

        Task OnGameDestroyed(PluginEvent eventId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnGameArgs)data;
            if (msg.Game == null)
                return Task.CompletedTask;
            if (!SupportedAppIds.Contains(msg.Game.ApplicationId))
                return Task.CompletedTask;

            return Game.OnGameDestroyed(msg.Game);
        }

        Task OnGameCreated(PluginEvent eventId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnPlayerGameArgs)data;
            if (msg.Player == null || msg.Game == null)
                return Task.CompletedTask;
            if (!SupportedAppIds.Contains(msg.Player.ApplicationId))
                return Task.CompletedTask;

            var client = msg.Player;
            var game = msg.Game;
            var playerExtraInfo = Player.GetPlayerExtraInfo(client.AccountId);

            // reset map version
            playerExtraInfo.CurrentMapVersion = 0;

            return Task.CompletedTask;
        }

        Task OnHostLeftGame(PluginEvent eventId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnPlayerGameArgs)data;
            if (msg.Player == null || msg.Game == null)
                return Task.CompletedTask;
            if (!SupportedAppIds.Contains(msg.Player.ApplicationId))
                return Task.CompletedTask;

            // close world if host left staging
            //if (msg.Game.WorldStatus == MediusWorldStatus.WorldStaging)
            //    return msg.Game.SetWorldStatus(MediusWorldStatus.WorldClosed);

            return Task.CompletedTask;
        }

        Task OnPlayerJoinedGame(PluginEvent eventId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnPlayerGameArgs)data;
            if (msg.Player == null || msg.Game == null)
                return Task.CompletedTask;
            if (!SupportedAppIds.Contains(msg.Player.ApplicationId))
                return Task.CompletedTask;

            var client = msg.Player;
            var game = msg.Game;
            var playerExtraInfo = Player.GetPlayerExtraInfo(client.AccountId);

            // reset map version
            playerExtraInfo.CurrentMapVersion = 0;

            // pass to game
            return Game.PlayerJoined(client, game);
        }

        Task OnPlayerPostWideStats(PluginEvent eventId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnPlayerWideStatsArgs)data;
            if (msg.Player == null || msg.Game == null)
                return Task.CompletedTask;
            if (!SupportedAppIds.Contains(msg.Player.ApplicationId))
                return Task.CompletedTask;

            // pass to game
            return Game.OnPlayerPostWideStats(msg);
        }

        async Task OnRecvCheatQuery(RT_MSG_TYPE msgId, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnMessageArgs)data;
            if (msg.Ignore || !msg.IsIncoming || msg.Player == null)
                return;

            if (!SupportedAppIds.Contains(msg.Player.ApplicationId))
                return;

            var cheatQuery = msg.Message as RT_MSG_SERVER_CHEAT_QUERY;
            if (cheatQuery == null)
                return;

            switch (cheatQuery.SequenceId)
            {
                case 101:
                    {
                        msg.Ignore = true;
                        await Patch.QueryForPatchResponse(msg.Player, cheatQuery);
                        break;
                    }
                default:
                    {
                        Host.Log(InternalLogLevel.WARN, $"Unhandled cheat query sequence id {cheatQuery.SequenceId}: {msg}");
                        break;
                    }
            }
        }

        async Task OnRecvCustomMessage(NetMessageTypes msgClass, byte msgType, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnMediusMessageArgs)data;
            if (msg.Ignore || !msg.IsIncoming || msg.Player == null)
                return;
            if (!SupportedAppIds.Contains(msg.Player.ApplicationId))
                return;

            var contents = (msg.Message as RawMediusMessage).Contents;
            var customMsgId = contents[0];
            msg.Ignore = true;

            using (var ms = new MemoryStream(contents, false))
            {
                using (var reader = new MessageReader(ms))
                {
                    // cgm custom messages begin at 100
                    if (customMsgId >= 100)
                    {
                        var game = msg.Player.CurrentGame;
                        if (game != null)
                        {
                            var metadata = await Game.GetGameMetadata(game);
                            if (metadata != null)
                            {
                                var mode = Modes.FindCustomModeById(metadata.GameConfig.GetRealCustomModeId());
                                if (mode != null)
                                    await mode.OnRecvCustomMessage(msg.Player, customMsgId, reader);
                            }
                        }
                    }
                    else
                    {
                        switch (customMsgId)
                        {
                            case 2: // request IRX modules
                                {
                                    var irxModulesRequest = new MapModulesRequestMessage();
                                    irxModulesRequest.Deserialize(reader);
                                    await Maps.SendMapModules(msg.Player, irxModulesRequest.Module1Start, irxModulesRequest.Module2Start);
                                    break;
                                }
                            case 4: // player responds with map override version
                                {
                                    var request = new SetMapOverrideResponseMessage();
                                    request.Deserialize(reader);

                                    await Player.SetPlayerMapVersion(msg.Player, request.ClientMapVersion);
                                    break;
                                }
                            case 5: // game started
                                {
                                    var game = msg.Player.CurrentGame;
                                    if (game != null && game.Host == msg.Player && game.WorldStatus == MediusWorldStatus.WorldStaging)
                                        await game.SetWorldStatus(MediusWorldStatus.WorldActive);
                                    break;
                                }
                            case 6: // set player patch config
                                {
                                    var request = new SetPlayerPatchConfigRequestMessage();
                                    request.Deserialize(reader);
                                    await Player.SetPatchConfig(msg.Player, request.Config);
                                    break;
                                }
                            case 7: // request patch
                                {
                                    await Patch.SendPatch(msg.Player);
                                    break;
                                }
                            case 9: // set player patch config
                                {
                                    if (msg.Player.CurrentGame != null && msg.Player.CurrentGame.Host == msg.Player && msg.Player.CurrentGame.WorldStatus <= MediusWorldStatus.WorldStaging)
                                    {
                                        var request = new SetGameConfigRequestMessage();
                                        request.Deserialize(reader);

                                        // try to update game config
                                        if (await Game.SetGameConfig(msg.Player.CurrentGame, request.Config))
                                        {
                                            // send new game config to other players in lobby
                                            await Game.BroadcastGameConfig(msg.Player.CurrentGame);
                                        }
                                    }
                                    break;
                                }
                            case 11: // 
                                {
                                    if (msg.Player.CurrentGame != null)
                                    {
                                        var request = new SetGameStateRequestMessage();
                                        request.Deserialize(reader);
                                        await Game.SetGameState(msg.Player.CurrentGame, request.State);
                                    }
                                    break;
                                }
                            case 12: // request for current custom map override
                                {
                                    var requestMessage = new GetMapOverrideRequestMessage();
                                    requestMessage.Deserialize(reader);
                                    await Game.SendMapOverride(msg.Player);
                                    break;
                                }
                            case 14: // download data response
                                {
                                    var downloadDataResponse = new DataDownloadResponseMessage();
                                    downloadDataResponse.Deserialize(reader);
                                    await Downloader.OnDataDownloadResponse(msg.Player, downloadDataResponse);
                                    break;
                                }
                            case 15: // update game data
                                {
                                    var request = new SetGameDataRequestMessage();
                                    request.Deserialize(reader);
                                    await Game.UpdateGameData(msg.Player, msg.Player.CurrentGame, request);
                                    break;
                                }
                            case 21: // game reached end scoreboard
                                {
                                    var game = msg.Player.CurrentGame;
                                    if (game != null && game.WorldStatus == MediusWorldStatus.WorldActive)
                                    {
                                        await game.SetWorldStatus(MediusWorldStatus.WorldClosed);
                                        await Game.OnGameComplete(game);
                                    }
                                    break;
                                }
                            case 22: // player request enter queue
                                {
                                    var request = new QueueBeginRequestMessage();
                                    request.Deserialize(reader);
                                    await Queue.OnQueueRequest(msg.Player, request.QueueId);
                                    break;
                                }
                            case 24: // player request queue information
                                {
                                    await Queue.OnGetMyQueue(msg.Player);
                                    break;
                                }
                            case 29: // player cast vote
                                {
                                    var request = new VoteRequestMessage();
                                    request.Deserialize(reader);
                                    var game = msg.Player.CurrentGame;
                                    if (game is CompGame compGame)
                                        await compGame.Vote(msg.Player, request);
                                    break;
                                }
                            case 36: // client wants custom mode payload
                                {
                                    var request = new CustomModePayloadRequest();
                                    request.Deserialize(reader);
                                    var game = msg.Player.CurrentGame;
                                    if (game != null)
                                    {
                                        await Game.SendGameMode(game, msg.Player);
                                        msg.Player.Queue(new CustomModePayloadResponse());
                                    }
                                    break;
                                }
                            default:
                                {
                                    Host.Log(InternalLogLevel.WARN, $"Unhandled custom msg id {customMsgId}: {msg}");
                                    break;
                                }
                        }
                    }
                }
            }
        }

        Task OnRecvUpdateClanStats(NetMessageTypes msgClass, byte msgType, object data)
        {
            var msg = (Server.Medius.PluginArgs.OnMediusMessageArgs)data;
            if (msg.Ignore || !msg.IsIncoming || msg.Player == null)
                return Task.CompletedTask;
            if (!SupportedAppIds.Contains(msg.Player.ApplicationId))
                return Task.CompletedTask;

            var request = (msg.Message as MediusUpdateClanStatsRequest);
            var stats = request.Stats;
            var oldCtag = Encoding.UTF8.GetString(stats, 0xB4, 4);
            var ctag = Encoding.UTF8.GetString(Enumerable.ToArray(Enumerable.Reverse(Utils.FromString(Encoding.UTF8.GetString(stats, 0, 0x18))))).Replace("\x00", "");

            // filter out invalid characters
            var re = new Regex(@"[\x01-\x07]|[\x10-\x1F]|[\x80-\xFF]");
            if (re.IsMatch(ctag))
            {
                var response = new MediusUpdateClanStatsResponse();
                response.MessageID = request.MessageID;
                response.StatusCode = MediusCallbackStatus.MediusClanNotFound;
                msg.Player.Queue(response);
                msg.Ignore = true;
                return Task.CompletedTask;
            }

            // color codes
            if (oldCtag == "!col")
            {
                ctag = ctag
                    .Replace("1", "\x08")
                    .Replace("2", "\x09")
                    .Replace("3", "\x0A")
                    .Replace("4", "\x0B")
                    .Replace("5", "\x0C")
                    .Replace("7", "\x0D")
                    .Replace("8", "\x0E")
                    .Replace("9", "\x0F")
                    ;
            }

            // convert to hex string
            ctag = BitConverter.ToString(Enumerable.ToArray(Enumerable.Reverse(Encoding.UTF8.GetBytes(ctag)))).Replace("-", "");

            using (var ms = new MemoryStream(stats, true))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    foreach (var i in Enumerable.Range(0, 0x18 - ctag.Length))
                    {
                        writer.Write((byte)0x30);
                    }

                    writer.WriteStr(ctag, ctag.Length + 1);
                }
            }

            return Task.CompletedTask;
        }
    }
}
