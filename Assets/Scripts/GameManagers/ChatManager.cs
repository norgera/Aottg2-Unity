﻿using System.Collections.Generic;
using UnityEngine;
using Weather;
using UI;
using Utility;
using CustomSkins;
using CustomLogic;
using ApplicationManagers;
using System.Diagnostics;
using Settings;
using Anticheat;
using Photon.Realtime;
using Photon.Pun;
using System;
using System.Reflection;
using System.Linq;
using Map;
using System.Collections;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;


namespace GameManagers
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    class CommandAttribute : Attribute
    {
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string Alias { get; set; } = null;
        public MethodInfo Command { get; set; } = null;
        public bool IsAlias { get; set; } = false;

        public CommandAttribute(CommandAttribute commandAttribute)
        {
            Name = commandAttribute.Name;
            Description = commandAttribute.Description;
            Alias = commandAttribute.Alias;
            Command = commandAttribute.Command;
            Alias = commandAttribute.Alias;
        }

        public CommandAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    class ChatManager : MonoBehaviour
    {
        private static ChatManager _instance;
        public static List<ChatMessage> Lines = new List<ChatMessage>();
        public static List<string> FeedLines = new List<string>();
        private static readonly int MaxLines = 30;
        public static Dictionary<ChatTextColor, string> ColorTags = new Dictionary<ChatTextColor, string>();
        private static readonly Dictionary<string, CommandAttribute> CommandsCache = new Dictionary<string, CommandAttribute>();
        private static string LastException;
        private static int LastExceptionCount;
        private static string _lastPartialName = "";
        private static int _lastSuggestionCount = 0;
        private static List<string> _currentSuggestions = new List<string>();
        private static int _currentSuggestionIndex = -1;

        public static void Init()
        {
            _instance = SingletonFactory.CreateSingleton(_instance);

            // Read all methods, filter out methods using CommandAttribute, create mapping from name/alias to CommandAttribute for later reference.
            MethodInfo[] infos = typeof(ChatManager).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            Type cmdAttrType = typeof(CommandAttribute);

            foreach (MethodInfo info in infos)
            {
                object[] attrs = info.GetCustomAttributes(cmdAttrType, false);
                if (attrs == null) continue;

                if (attrs.Length > 0)
                {
                    foreach (object attr in attrs)
                    {
                        if (attr is CommandAttribute cmdAttr)
                        {
                            cmdAttr.Command = info;

                            CommandsCache.Add(cmdAttr.Name, cmdAttr);

                            // Create second mapping from alias to cmd, has to be a separate object flagged as alias.
                            // This lets us ignore alias's later on in the help function.
                            if (cmdAttr.Alias != null)
                            {
                                CommandAttribute alias = new CommandAttribute(cmdAttr);
                                alias.IsAlias = true;
                                CommandsCache.Add(alias.Alias, alias);
                            }
                        }
                    }
                }
            }

        }

        public static void Reset()
        {
            Lines.Clear();
            LastException = string.Empty;
            LastExceptionCount = 0;
            FeedLines.Clear();
            LoadTheme();
        }

        public static void Clear()
        {
            Lines.Clear();
            LastException = string.Empty;
            LastExceptionCount = 0;
            FeedLines.Clear();
            GetChatPanel().Sync();
            var feedPanel = GetFeedPanel();
            if (feedPanel != null)
                feedPanel.Sync();
        }

        public static bool IsChatActive()
        {
            return GetChatPanel().IsInputActive();
        }

        public static bool IsChatAvailable()
        {
            return SceneLoader.SceneName == SceneName.InGame && UIManager.CurrentMenu != null && UIManager.CurrentMenu is InGameMenu;
        }

        public static void SendChatAll(string message, ChatTextColor color = ChatTextColor.Default)
        {
            message = GetColorString(message, color);
            RPCManager.PhotonView.RPC("ChatRPC", RpcTarget.All, new object[] { message });
        }

        public static void SendChat(string message, Player player, ChatTextColor color = ChatTextColor.Default)
        {
            message = GetColorString(message, color);
            RPCManager.PhotonView.RPC("ChatRPC", player, new object[] { message });
        }

        public static void OnChatRPC(string message, PhotonMessageInfo info)
        {
            if (InGameManager.MuteText.Contains(info.Sender.ActorNumber))
                return;
            
            var chatMessage = new ChatMessage
            {
                RawMessage = GetIDString(info.Sender.ActorNumber) + message,
                SenderID = info.Sender.ActorNumber,
                IsSystem = false,
                UtcTimestamp = DateTime.UtcNow.AddSeconds(-Util.GetPhotonTimestampDifference(info.SentServerTime, PhotonNetwork.Time))
            };
            
            Lines.Add(chatMessage);
            if (Lines.Count > MaxLines)
                Lines.RemoveAt(0);
            
            if (IsChatAvailable())
            {
                var panel = GetChatPanel();
                if (panel != null)
                    panel.AddLine(chatMessage.GetFormattedMessage());
            }
        }

        public static void OnAnnounceRPC(string message)
        {
            var chatMessage = new ChatMessage
            {
                RawMessage = message,
                IsSystem = true,
                Color = ChatTextColor.System,
                UtcTimestamp = DateTime.UtcNow
            };
            AddLine(chatMessage);
        }

        public static void AddLine(string line)
        {
            AddLine(line, ChatTextColor.Default);
        }

        public static void AddLine(string line, bool hasTimestamp)
        {
            AddLine(line, ChatTextColor.Default, hasTimestamp);
        }

        public static void AddLine(string line, ChatTextColor color, bool hasTimestamp = false)
        {
            line = line.FilterSizeTag();
            var chatMessage = new ChatMessage
            {
                RawMessage = GetColorString(line, color),
                Color = color,
                IsSystem = true,
                UtcTimestamp = DateTime.UtcNow
            };
            
            AddLine(chatMessage);
        }

        private static void AddLine(ChatMessage message)
        {
            Lines.Add(message);
            if (Lines.Count > MaxLines)
                Lines.RemoveAt(0);
            if (IsChatAvailable())
            {
                var panel = GetChatPanel();
                if (panel != null)
                    panel.AddLine(message.GetFormattedMessage());
            }
        }

        public static void AddException(string line)
        {
            if (LastException == line)
            {
                LastExceptionCount++;
                var message = new ChatMessage
                {
                    RawMessage = GetColorString(line + "(" + LastExceptionCount.ToString() + ")", ChatTextColor.Error),
                    Color = ChatTextColor.Error,
                    IsSystem = true,
                    UtcTimestamp = DateTime.UtcNow
                };
                ReplaceLastLine(message);
            }
            else
            {
                LastException = line;
                LastExceptionCount = 0;
                AddLine(GetColorString(line, ChatTextColor.Error), ChatTextColor.Error);
            }
        }

        public static void ReplaceLastLine(ChatMessage message)
        {
            if (Lines.Count > 0)
            {
                Lines[Lines.Count - 1] = message;
                if (IsChatAvailable())
                {
                    var panel = GetChatPanel();
                    if (panel != null)
                        panel.ReplaceLastLine(message.GetFormattedMessage());
                }
            }
            else
            {
                AddLine(message);
            }
        }

        public static void AddLine(string line, int senderID)
        {
            line = line.FilterSizeTag();
            var message = new ChatMessage
            {
                RawMessage = GetIDString(senderID) + line,
                SenderID = senderID,
                IsSystem = false
            };
            
            Lines.Add(message);
            if (Lines.Count > MaxLines)
                Lines.RemoveAt(0);
            if (IsChatAvailable())
            {
                var panel = GetChatPanel();
                if (panel != null)
                    panel.AddLine(message.GetFormattedMessage());
            }
        }

        public static void AddFeed(string line)
        {
            if (!IsChatAvailable())
                return;
            var feed = GetFeedPanel();
            if (feed == null)
            {
                AddLine(line);
                return;
            }
            line = line.FilterSizeTag();
            FeedLines.Add(line);
            if (FeedLines.Count > MaxLines)
                FeedLines.RemoveAt(0);
            feed.AddLine(line);
        }

        public static void IsTalking(Player player, bool isSpeaking)
        {
            if (!IsChatAvailable())
                return;
            var voiceChatPanel = GetVoiceChatPanel();
            if (voiceChatPanel == null)
                return;
            if (isSpeaking)
                voiceChatPanel.AddPlayer(player);
            else
                voiceChatPanel.RemovePlayer(player);
        }

        public static void LoadTheme()
        {
            ColorTags.Clear();
            foreach (ChatTextColor color in Util.EnumToList<ChatTextColor>())
            {
                Color c = UIManager.GetThemeColor("ChatPanel", "TextColor", color.ToString());
                ColorTags.Add(color, string.Format("{0:X2}{1:X2}{2:X2}", (int)(c.r * 255), (int)(c.g * 255), (int)(c.b * 255)));
            }
        }

        public static void HandleInput(string input)
        {
            if (input == string.Empty)
                return;
            var response = CustomLogicManager.Evaluator.OnChatInput(input);
            if (response is bool && ((bool)response == true))
                return;
            if (input.StartsWith("/"))
            {
                if (input.Length == 1)
                    return;
                string[] args = input.Substring(1).Split(' ');
                if (args.Length > 0)
                    HandleCommand(args);
            }
            else
            {
                string name = PhotonNetwork.LocalPlayer.GetStringProperty(PlayerProperty.Name);
                string processedMessage = ProcessMentions(input);
                SendChatAll(name + ": " + processedMessage);
            }
        }

        public static string GetAutocompleteSuggestion(string currentInput)
        {
            if (currentInput.StartsWith("/"))
            {
                return HandleCommandSuggestions(currentInput);
            }
            
            int lastAtSymbol = currentInput.LastIndexOf('@');
            if (lastAtSymbol != -1)
            {
                return HandlePlayerMentionSuggestions(currentInput, lastAtSymbol);
            }

            ClearLastSuggestions();
            return null;
        }

        private static string HandleCommandSuggestions(string input)
        {
            string[] parts = input.Split(' ');
            
            // First part - command name completion
            if (parts.Length == 1)
            {
                return HandleCommandNameSuggestions(input.Substring(1).ToLower());
            }
            // Second part - player ID completion for relevant commands
            else if (parts.Length == 2 && IsPlayerIdCommand(parts[0].Substring(1)))
            {
                return HandlePlayerIdSuggestions(parts[0], parts[1]);
            }

            ClearLastSuggestions();
            return null;
        }

        private static string HandleCommandNameSuggestions(string partialCommand)
        {
            var matchingCommands = CommandsCache
                .Where(cmd => 
                    !cmd.Value.IsAlias && 
                    cmd.Key.ToLower().StartsWith(partialCommand) &&
                    !CommandsCache.Any(other => 
                        other.Value.IsAlias && 
                        other.Value.Name == cmd.Key && 
                        other.Key.ToLower().StartsWith(partialCommand)))
                    .OrderBy(cmd => cmd.Key)
                    .ToList();

            if (partialCommand != _lastPartialName || _lastSuggestionCount == 0)
            {
                _lastPartialName = partialCommand;
                ClearLastSuggestions();
                
                if (matchingCommands.Count > 0)
                {
                    foreach (var cmd in matchingCommands)
                    {
                        string description = cmd.Value.Description;
                        string prefix = "/" + cmd.Key + ": ";
                        if (description.StartsWith(prefix))
                        {
                            description = description.Substring(prefix.Length);
                        }
                        
                        AddLine(new ChatMessage {
                            RawMessage = GetColorString($"/{cmd.Key}: {description}", ChatTextColor.System),
                            Color = ChatTextColor.System,
                            IsSystem = true,
                            IsSuggestion = true,
                            UtcTimestamp = DateTime.UtcNow
                        });
                    }

                    _lastSuggestionCount = matchingCommands.Count;
                    UpdateChatPanel();
                }
            }
            
            if (matchingCommands.Count == 1 && partialCommand.Length >= 2)
            {
                return "/" + matchingCommands[0].Key;
            }
            
            return null;
        }

        private static string HandlePlayerIdSuggestions(string command, string partialId)
        {
            if (partialId != _lastPartialName || _lastSuggestionCount == 0)
            {
                ShowPlayerSuggestions(partialId.ToLower());
            }
            
            var matchingPlayers = GetMatchingPlayers(partialId);
            if (matchingPlayers.Count == 1 && !string.IsNullOrEmpty(partialId) && partialId.Length >= 1)
            {
                return command + " " + matchingPlayers[0].ActorNumber;
            }
            
            return null;
        }

        private static string HandlePlayerMentionSuggestions(string input, int lastAtSymbol)
        {
            string partialName = input.Substring(lastAtSymbol + 1).ToLower();
            
            if (partialName != _lastPartialName || _lastSuggestionCount == 0)
            {
                ShowPlayerSuggestions(partialName);
            }
            
            var matchingPlayers = GetMatchingPlayers(partialName);
            if (matchingPlayers.Count == 1 && partialName.Length >= 2)
            {
                return matchingPlayers[0].GetStringProperty(PlayerProperty.Name).FilterSizeTag().StripRichText();
            }
            
            return null;
        }

        private static List<Player> GetMatchingPlayers(string partial)
        {
            return PhotonNetwork.PlayerList
                .Where(p => 
                {
                    if (string.IsNullOrEmpty(partial)) return true;
                    string playerId = p.ActorNumber.ToString();
                    string playerName = p.GetStringProperty(PlayerProperty.Name).FilterSizeTag().StripRichText().ToLower();
                    return playerId.StartsWith(partial.ToLower()) || playerName.StartsWith(partial.ToLower());
                })
                .ToList();
        }

        private static void ShowPlayerSuggestions(string partial)
        {
            ClearLastSuggestions();
            _lastPartialName = partial;
            
            var players = GetMatchingPlayers(partial);
            if (players.Count > 0)
            {
                AddLine(new ChatMessage {
                    RawMessage = GetColorString("Matching players:", ChatTextColor.System),
                    Color = ChatTextColor.System,
                    IsSystem = true,
                    IsSuggestion = true,
                    UtcTimestamp = DateTime.UtcNow
                });
                
                foreach (var player in players)
                {
                    AddLine(new ChatMessage {
                        RawMessage = GetColorString($"{player.ActorNumber}: {player.GetStringProperty(PlayerProperty.Name).FilterSizeTag()}", ChatTextColor.System),
                        Color = ChatTextColor.System,
                        IsSystem = true,
                        IsSuggestion = true,
                        UtcTimestamp = DateTime.UtcNow
                    });
                }

                _lastSuggestionCount = players.Count + 1;
                UpdateChatPanel();
            }
        }

        public static void UpdateChatPanel()
        {
            if (IsChatAvailable())
            {
                var panel = GetChatPanel();
                if (panel != null)
                    panel.Sync();
            }
        }

        private static void HandleCommand(string[] args)
        {
            if (CommandsCache.TryGetValue(args[0], out CommandAttribute cmdAttr))
            {
                MethodInfo info = cmdAttr.Command;
                if (info.IsStatic)
                {
                    info.Invoke(null, new object[1] { args });
                }
                else
                {
                    info.Invoke(_instance, new object[1] { args });
                }
            }
            else
            {
                AddLine($"Command {args[0]} not found, try /help to see a list of commands.", ChatTextColor.Error);
            }
        }

        [CommandAttribute("restart", "/restart: Restarts the game.", Alias = "r")]
        private static void Restart(string[] args)
        {
            if (CheckMC())
                InGameManager.RestartGame();
        }

        [CommandAttribute("closelobby", "/closelobby: Kicks all players and ends the lobby.")]
        private static void CloseLobby(string[] args)
        {
            if (CheckMC())
            {
                foreach (var player in PhotonNetwork.PlayerList)
                {
                    if (!player.IsLocal)
                    {
                        KickPlayer(player, false);
                    }
                }
                _instance.StartCoroutine(_instance.WaitAndLeave());
            }
        }

        private IEnumerator WaitAndLeave()
        {
            yield return new WaitForSeconds(2f);
            InGameManager.LeaveRoom();
        }

        [CommandAttribute("clear", "/clear: Clears the chat window.", Alias = "c")]
        private static void Clear(string[] args)
        {
            Clear();
        }

        [CommandAttribute("reviveall", "/reviveall: Revive all players.", Alias = "rva")]
        private static void ReviveAll(string[] args)
        {
            if (CheckMC())
            {
                RPCManager.PhotonView.RPC("SpawnPlayerRPC", RpcTarget.All, new object[] { false });
                SendChatAll("All players have been revived by master client.", ChatTextColor.System);
            }
        }

        [CommandAttribute("revive", "/revive [ID]: Revives the player with ID", Alias = "rv")]
        private static void Revive(string[] args)
        {
            if (CheckMC())
            {
                var player = GetPlayer(args);
                if (player != null)
                {
                    RPCManager.PhotonView.RPC("SpawnPlayerRPC", player, new object[] { false });
                    SendChat("You have been revived by master client.", player, ChatTextColor.System);
                    AddLine(player.GetStringProperty(PlayerProperty.Name) + " has been revived.", ChatTextColor.System);
                }
            }
        }

        [CommandAttribute("pm", "/pm [ID]: Private message player with ID")]
        private static void PrivateMessage(string[] args)
        {
            var player = GetPlayer(args);
            if (args.Length > 2 && player != null)
            {
                string[] msgArgs = new string[args.Length - 2];
                Array.ConstrainedCopy(args, 2, msgArgs, 0, msgArgs.Length);
                string message = string.Join(' ', msgArgs);

                SendChat("From " + PhotonNetwork.LocalPlayer.GetStringProperty(PlayerProperty.Name) + ": " + message, player);
                AddLine("To " + player.GetStringProperty(PlayerProperty.Name) + ": " + message);
            }
        }

        [CommandAttribute("kick", "/kick [ID]: Kick the player with ID")]
        private static void Kick(string[] args)
        {
            var player = GetPlayer(args);
            if (player == null) return;
            if (PhotonNetwork.IsMasterClient)
                KickPlayer(player);
            else if (CanVoteKick(player))
                RPCManager.PhotonView.RPC(nameof(RPCManager.VoteKickRPC), RpcTarget.MasterClient, new object[] { player.ActorNumber });
        }

        [CommandAttribute("ban", "/ban [ID]: Ban the player with ID")]
        private static void Ban(string[] args)
        {
            var player = GetPlayer(args);
            if (player == null) return;
            if (PhotonNetwork.IsMasterClient)
                KickPlayer(player, ban: true);
        }

        private static bool CanVoteKick(Player player)
        {
            if (!SettingsManager.InGameCurrent.Misc.AllowVoteKicking.Value)
            {
                AddLine("Server does not allow vote kicking.", ChatTextColor.Error);
                return false;
            }
            if (player == PhotonNetwork.LocalPlayer)
            {
                AddLine("Cannot vote to kick yourself.", ChatTextColor.Error);
                return false;
            }
            if (player.IsMasterClient)
            {
                AddLine("Cannot vote to kick the Master Client.", ChatTextColor.Error);
                return false;
            }
            return true;
        }

        [CommandAttribute("maxplayers", "/maxplayers [num]: Sets room's max player count.")]
        private static void MaxPlayers(string[] args)
        {
            if (CheckMC())
            {
                int players;
                if (args.Length > 1 && int.TryParse(args[1], out players) && players >= 0)
                {
                    PhotonNetwork.CurrentRoom.MaxPlayers = players;
                    AddLine("Max players set to " + players.ToString() + ".", ChatTextColor.System);
                }
                else
                    AddLine("Max players must be >= 0.", ChatTextColor.Error);
            }
        }

        [CommandAttribute("mute", "/mute [ID]: Mute player with ID.")]
        private static void Mute(string[] args)
        {
            var player = GetPlayer(args);
            if (player != null)
            {
                MutePlayer(player, "Emote");
                MutePlayer(player, "Text");
                MutePlayer(player, "Voice");
            }
        }

        [CommandAttribute("unmute", "/unmute [ID]: Unmute player with ID.")]
        private static void Unmute(string[] args)
        {
            var player = GetPlayer(args);
            if (player != null)
            {
                UnmutePlayer(player, "Emote");
                UnmutePlayer(player, "Text");
                UnmutePlayer(player, "Voice");
            }
        }

        [CommandAttribute("nextsong", "/nextsong: Play next song in playlist.")]
        private static void NextSong(string[] args)
        {
            MusicManager.ChatNextSong();
        }

        [CommandAttribute("pause", "/pause: Pause the multiplayer game.")]
        private static void Pause(string[] args)
        {
            if (CheckMC())
                ((InGameManager)SceneLoader.CurrentGameManager).PauseGame();
        }

        [CommandAttribute("unpause", "/unpause: Unpause the multiplayer game.")]
        private static void Unpause(string[] args)
        {
            if (CheckMC())
                ((InGameManager)SceneLoader.CurrentGameManager).StartUnpauseGame();
        }

        [CommandAttribute("resetkd", "/resetkd: Reset your own stats.")]
        private static void Resetkd(string[] args)
        {
            InGameManager.ResetPlayerKD(PhotonNetwork.LocalPlayer);
        }

        [CommandAttribute("resetkdall", "/resetkdall: Reset all player stats.")]
        private static void Resetkdall(string[] args)
        {
            RPCManager.PhotonView.RPC("ResetKDRPC", RpcTarget.All);
        }


        [CommandAttribute("help", "/help [page(optional)]: Displays command usage.")]
        private static void Help(string[] args)
        {
            int displayPage = 1;
            int elementsPerPage = 7;
            if (args.Length >= 2)
            {
                int.TryParse(args[1], out displayPage);
                if (displayPage == 0)
                {
                    displayPage = 1;
                }
            }

            int totalPages = (int)Math.Ceiling((double)CommandsCache.Count / elementsPerPage);
            if (displayPage < 1 || displayPage > totalPages)
            {
                AddLine($"Page {displayPage} does not exist.", ChatTextColor.Error);
                return;
            }

            List<CommandAttribute> pageElements = Util.PaginateDictionary(CommandsCache, displayPage, elementsPerPage);
            string help = "----Command list----" + "\n";
            foreach (CommandAttribute element in pageElements)
            {
                if (element.IsAlias == false)
                {
                    help += element.Description + "\n";
                }
            }

            help += $"Page {displayPage} / {totalPages}";

            AddLine(help, ChatTextColor.System);
        }

        [CommandAttribute("save", "/save: Save chat history to Downloads folder")]
        private static void SaveChatHistory(string[] args)
        {
            try
            {
                DateTime timestamp = DateTime.UtcNow;
                string filename = $"chat_history_{timestamp:yyyy-MM-dd_HH-mm-ss}.txt";
                
                string downloadsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads"
                );
                string filePath = Path.Combine(downloadsPath, filename);
                
                // Collect chat content
                string chatContent = string.Join("\n", Lines.Select(line => 
                    System.Text.RegularExpressions.Regex.Replace(line.GetFormattedMessage(), "<.*?>", string.Empty)
                ));
                
                // Calculate hash of content
                string hash;
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(chatContent);
                    byte[] hashBytes = sha256.ComputeHash(bytes);
                    hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
                
                // Create file content with hash header
                string fileContent = $"[HASH:{hash}]\n[TIME:{timestamp:yyyy-MM-dd HH:mm:ss UTC}]\n\n{chatContent}";
                
                // Write file
                File.WriteAllText(filePath, fileContent);
                
                // Set file as read-only
                File.SetAttributes(filePath, FileAttributes.ReadOnly);
                
                AddLine($"Chat history saved to Downloads/{filename}", ChatTextColor.System);
            }
            catch (Exception ex)
            {
                AddLine($"Failed to save chat history: {ex.Message}", ChatTextColor.Error);
            }
        }

        [CommandAttribute("verify", "/verify [filename]: Verify if a chat history file has been modified")]
        private static void VerifyChatHistory(string[] args)
        {
            if (args.Length != 2)
            {
                AddLine("Usage: /verify [filename]", ChatTextColor.Error);
                return;
            }

            try
            {
                string filename = args[1];
                string downloadsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads"
                );
                string filePath = Path.Combine(downloadsPath, filename);

                if (!File.Exists(filePath))
                {
                    AddLine($"File not found: {filename}", ChatTextColor.Error);
                    return;
                }

                // Read file content
                string[] lines = File.ReadAllLines(filePath);
                
                // File must have at least 4 lines (hash, time, blank line, and content)
                if (lines.Length < 4)
                {
                    AddLine("Invalid file format.", ChatTextColor.Error);
                    return;
                }

                // Extract stored hash
                if (!lines[0].StartsWith("[HASH:") || !lines[0].EndsWith("]"))
                {
                    AddLine("Invalid file format: missing hash header.", ChatTextColor.Error);
                    return;
                }
                string storedHash = lines[0].Substring(6, lines[0].Length - 7);

                // Get content (everything after the blank line)
                string content = string.Join("\n", lines.Skip(3));

                // Calculate hash of current content
                string currentHash;
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
                    byte[] hashBytes = sha256.ComputeHash(bytes);
                    currentHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }

                // Compare hashes
                if (currentHash == storedHash)
                {
                    AddLine("File verification successful - content has not been modified.", ChatTextColor.System);
                }
                else
                {
                    AddLine("Warning: File has been modified!", ChatTextColor.Error);
                }
            }
            catch (Exception ex)
            {
                AddLine($"Failed to verify file: {ex.Message}", ChatTextColor.Error);
            }
        }

        public static void KickPlayer(Player player, bool print = true, bool ban = false, string reason = ".")
        {
            if (PhotonNetwork.IsMasterClient && player != PhotonNetwork.LocalPlayer)
            {
                AnticheatManager.KickPlayer(player, ban);
                if (print)
                {
                    if (ban)
                        SendChatAll($"{player.GetStringProperty(PlayerProperty.Name)} has been banned{reason}", ChatTextColor.System);
                    else
                        SendChatAll($"{player.GetStringProperty(PlayerProperty.Name)} has been kicked{reason}", ChatTextColor.System);
                }
            }
        }

        public static void VoteKickPlayer(Player voter, Player target)
        {
            if (target == null) return;
            if (target.IsMasterClient) return;
            if (!PhotonNetwork.IsMasterClient) return;
            if (!SettingsManager.InGameCurrent.Misc.AllowVoteKicking.Value) return;

            var result = AnticheatManager.TryVoteKickPlayer(voter, target);
            var msg = result.ToString();
            RPCManager.PhotonView.RPC(nameof(RPCManager.AnnounceRPC), voter, new object[] { msg });
            if (result.IsSuccess)
                SendChatAll(target.GetStringProperty(PlayerProperty.Name) + " has been vote kicked.", ChatTextColor.System);
        }

        public static void MutePlayer(Player player, string muteType)
        {
            if (player == PhotonNetwork.LocalPlayer) return;

            if (muteType == "Emote")
            {
                InGameManager.MuteEmote.Add(player.ActorNumber);
            }
            else if (muteType == "Text")
            {
                InGameManager.MuteText.Add(player.ActorNumber);
            }
            else if (muteType == "Voice")
            {
                InGameManager.MuteVoiceChat.Add(player.ActorNumber);
            }

            AddLine($"{player.GetStringProperty(PlayerProperty.Name)} has been muted ({muteType}).", ChatTextColor.System);
        }

        public static void UnmutePlayer(Player player, string muteType)
        {
            if (player == PhotonNetwork.LocalPlayer) return;

            if (muteType == "Emote" && InGameManager.MuteEmote.Contains(player.ActorNumber))
            {
                InGameManager.MuteEmote.Remove(player.ActorNumber);
            }
            else if (muteType == "Text" && InGameManager.MuteText.Contains(player.ActorNumber))
            {
                InGameManager.MuteText.Remove(player.ActorNumber);
            }
            else if (muteType == "Voice" && InGameManager.MuteVoiceChat.Contains(player.ActorNumber))
            {
                InGameManager.MuteVoiceChat.Remove(player.ActorNumber);
            }

            AddLine($"{player.GetStringProperty(PlayerProperty.Name)} has been unmuted ({muteType}).", ChatTextColor.System);
        }

        public static void SetPlayerVolume(Player player, float volume)
        {
            if (player == PhotonNetwork.LocalPlayer)
                return;
            if (InGameManager.VoiceChatVolumeMultiplier.ContainsKey(player.ActorNumber))
                if (InGameManager.VoiceChatVolumeMultiplier[player.ActorNumber] == volume)
                    return;
            InGameManager.VoiceChatVolumeMultiplier[player.ActorNumber] = volume;
        }

        private static Player GetPlayer(string stringID)
        {
            int id = -1;
            if (int.TryParse(stringID, out id) && PhotonNetwork.CurrentRoom.GetPlayer(id, true) != null)
            {
                var player = PhotonNetwork.CurrentRoom.GetPlayer(id, true);
                return player;
            }
            AddLine("Invalid player ID.", ChatTextColor.Error);
            return null;
        }

        public static Player GetPlayer(string[] args)
        {
            int id = -1;
            if (args.Length > 1 && int.TryParse(args[1], out id) && PhotonNetwork.CurrentRoom.GetPlayer(id, true) != null)
            {
                var player = PhotonNetwork.CurrentRoom.GetPlayer(id, true);
                return player;
            }
            AddLine("Invalid player ID.", ChatTextColor.Error);
            return null;
        }

        private static bool CheckMC()
        {
            if (PhotonNetwork.IsMasterClient)
                return true;
            AddLine("Must be master client to use that command.", ChatTextColor.Error);
            return false;
        }

        private static ChatPanel GetChatPanel()
        {
            return ((InGameMenu)UIManager.CurrentMenu).ChatPanel;
        }

        private static FeedPanel GetFeedPanel()
        {
            return ((InGameMenu)UIManager.CurrentMenu).FeedPanel;
        }

        private static VoiceChatPanel GetVoiceChatPanel()
        {
            return ((InGameMenu)UIManager.CurrentMenu).VoiceChatPanel;
        }

        private static KDRPanel GetKDRPanel()
        {
            return ((InGameMenu)UIManager.CurrentMenu).KDRPanel;
        }

        public static string GetIDString(int id, bool includeMC = false, bool myPlayer = false)
        {
            string str = "[" + id.ToString() + "] ";
            if (includeMC)
                str = "[M]" + str;
            if (myPlayer)
                return GetColorString(str, ChatTextColor.MyPlayer);
            return GetColorString(str, ChatTextColor.ID);
        }

        public static string GetColorString(string str, ChatTextColor color)
        {
            if (color == ChatTextColor.Default)
                return str;
            return "<color=#" + ColorTags[color] + ">" + str + "</color>";
        }

        private static string GetTimeString(DateTime time)
        {
            return time.ToString("HH:mm");
        }

        private void Update()
        {
            if (IsChatAvailable() && !InGameMenu.InMenu() && !DebugConsole.Enabled)
            {
                var chatPanel = GetChatPanel();
                var key = SettingsManager.InputSettings.General.Chat;
                if (key.GetKeyDown())
                {
                    if (chatPanel.IgnoreNextActivation)
                        chatPanel.IgnoreNextActivation = false;
                    else
                        chatPanel.Activate();
                }
            }
        }

        public static void ResetTabCompletion()
        {
            _currentSuggestionIndex = -1;
            _currentSuggestions.Clear();
            _lastPartialName = "";
        }

        public static void ClearLastSuggestions()
        {
            if (_lastSuggestionCount > 0 && Lines.Count >= _lastSuggestionCount)
            {
                Lines.RemoveRange(Lines.Count - _lastSuggestionCount, _lastSuggestionCount);
                _lastSuggestionCount = 0;
                _lastPartialName = "";
                
                if (IsChatAvailable())
                {
                    var panel = GetChatPanel();
                    if (panel != null)
                        panel.Sync();
                }
            }
        }

        public static string GetNextTabCompletion(string currentInput)
        {
            if (currentInput.StartsWith("/"))
            {
                string[] parts = currentInput.Split(' ');
                
                // If we're on the first part, handle command completion
                if (parts.Length == 1)
                {
                    string partialCommand = currentInput.Substring(1).ToLower();
                    
                    // Only refresh suggestions if we don't have any or if partial command changed
                    if (_currentSuggestions.Count == 0 || !partialCommand.Equals(_lastPartialName))
                    {
                        _currentSuggestions = CommandsCache
                            .Where(cmd => !cmd.Value.IsAlias && cmd.Key.ToLower().StartsWith(partialCommand))
                            .Select(cmd => "/" + cmd.Key)
                            .OrderBy(cmd => cmd)
                            .ToList();
                        _currentSuggestionIndex = -1;
                        _lastPartialName = partialCommand;
                    }

                    if (_currentSuggestions.Count > 0)
                    {
                        _currentSuggestionIndex = (_currentSuggestionIndex + 1) % _currentSuggestions.Count;
                        return _currentSuggestions[_currentSuggestionIndex];
                    }
                }
                // If we're on the second part and the command requires a player ID
                else if (parts.Length == 2 && IsPlayerIdCommand(parts[0].Substring(1)))
                {
                    string partialId = parts[1].ToLower();
                    string beforeId = parts[0] + " ";
                    
                    // Only refresh suggestions if we don't have any or if partial id changed
                    if (_currentSuggestions.Count == 0 || !partialId.Equals(_lastPartialName))
                    {
                        _currentSuggestions = GetMatchingPlayers(partialId)
                            .Select(p => beforeId + p.ActorNumber)
                            .ToList();
                        _currentSuggestionIndex = -1;
                        _lastPartialName = partialId;
                    }

                    if (_currentSuggestions.Count > 0)
                    {
                        _currentSuggestionIndex = (_currentSuggestionIndex + 1) % _currentSuggestions.Count;
                        return _currentSuggestions[_currentSuggestionIndex];
                    }
                }
            }
            else
            {
                // Handle @ mentions
                int lastAtSymbol = currentInput.LastIndexOf('@');
                if (lastAtSymbol != -1)
                {
                    string beforeAt = currentInput.Substring(0, lastAtSymbol + 1);
                    string partialName = currentInput.Substring(lastAtSymbol + 1).ToLower();
                    
                    // Only refresh suggestions if we don't have any or if partial name changed
                    if (_currentSuggestions.Count == 0 || !partialName.Equals(_lastPartialName))
                    {
                        _currentSuggestions = GetMatchingPlayers(partialName)
                            .Select(p => beforeAt + p.GetStringProperty(PlayerProperty.Name).FilterSizeTag().StripRichText())
                            .ToList();
                        _currentSuggestionIndex = -1;
                        _lastPartialName = partialName;
                    }

                    if (_currentSuggestions.Count > 0)
                    {
                        _currentSuggestionIndex = (_currentSuggestionIndex + 1) % _currentSuggestions.Count;
                        return _currentSuggestions[_currentSuggestionIndex];
                    }
                }
            }

            return null;
        }

        private static bool IsPlayerIdCommand(string command)
        {
            // List of commands that take a player ID as their first argument
            string[] playerIdCommands = new string[] {
                "pm", "kick", "ban", "mute", "unmute", "revive"
            };
            return playerIdCommands.Contains(command.ToLower());
        }

        private static string ProcessMentions(string message)
        {
            int index = message.IndexOf('@');
            while (index != -1)
            {
                // Find the end of the mention (space or end of string)
                int endIndex = message.IndexOf(' ', index);
                if (endIndex == -1)
                    endIndex = message.Length;

                // Get the mentioned name
                string mention = message.Substring(index + 1, endIndex - index - 1);

                // Find matching player
                var matchingPlayers = PhotonNetwork.PlayerList
                    .Where(p => p.GetStringProperty(PlayerProperty.Name)
                        .FilterSizeTag()
                        .StripRichText()
                        .ToLower()
                        .StartsWith(mention.ToLower()))
                    .ToList();

                // If exactly one match, replace with colored name
                if (matchingPlayers.Count == 1)
                {
                    string playerName = matchingPlayers[0].GetStringProperty(PlayerProperty.Name).FilterSizeTag();
                    string coloredName = GetColorString("@" + playerName, ChatTextColor.System);
                    message = message.Substring(0, index) + coloredName + message.Substring(endIndex);
                    
                    // Update index to continue search after the replacement
                    index = message.IndexOf('@', index + coloredName.Length);
                }
                else
                {
                    // No unique match, continue searching after this @
                    index = message.IndexOf('@', index + 1);
                }
            }
            
            return message;
        }
    }

    public enum ChatTextColor
    {
        Default,
        ID,
        MyPlayer,
        System,
        Error,
        TeamRed,
        TeamBlue
    }

    public class ChatMessage
    {
        public string RawMessage { get; set; }
        public int SenderID { get; set; }
        public ChatTextColor Color { get; set; }
        public bool IsSystem { get; set; }
        public DateTime UtcTimestamp { get; set; }
        public bool IsSuggestion { get; set; }

        public string GetFormattedMessage()
        {
            string result = RawMessage;
            if (SettingsManager.UISettings.ShowChatTimestamp.Value && !IsSuggestion)
            {
                DateTime localTime = UtcTimestamp.ToLocalTime();
                string timestamp = localTime.ToString("HH:mm");
                string timestampStr = ChatManager.GetColorString($"[{timestamp}] ", ChatTextColor.System);
                result = timestampStr + result;
            }
            return result;
        }
    }
}
