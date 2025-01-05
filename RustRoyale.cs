using System;
using System.Collections.Generic;
using System.IO;
using Oxide.Core;
using Oxide.Core.Libraries;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RustRoyale", "Potaetobag", "1.0.1"), Description("Rust Royale custom tournament game mode with point-based scoring system.")]
    class RustRoyale : RustPlugin
    {
        #region Configuration
        private ConfigData Configuration;

        private class ConfigData
        {
            public string DiscordWebhookUrl { get; set; } = "";
            public string ChatIconSteamId { get; set; } = "76561198332731279";
            public string ChatUsername { get; set; } = "[RustRoyale]";
            public bool AutoStartEnabled { get; set; } = true;
            public string StartDay { get; set; } = "Friday";
            public int StartHour { get; set; } = 14;
            public int DurationHours { get; set; } = 72;
            public Dictionary<string, int> ScoreRules { get; set; } = new Dictionary<string, int>
            {
                {"KILL", 3},     // Human player kills another human player
                {"DEAD", -3},    // Human player is killed by another human player
                {"JOKE", -1},    // Death by traps, NPCs, self-inflicted damage
                {"NPC", 1},      // Kill an NPC (Murderer, Zombie, Scientist, Scarecrow)
                {"ENT", 5}       // Kill a Helicopter or Bradley Tank
            };
        }

        protected override void LoadDefaultConfig()
        {
            Configuration = new ConfigData();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(Configuration, true);

        protected override void LoadConfig()
        {
            try
            {
                base.LoadConfig();
                Configuration = Config.ReadObject<ConfigData>();
            }
            catch
            {
                PrintWarning("Configuration file is corrupted or missing. Creating a new one.");
                LoadDefaultConfig();
            }
        }

        private void ValidateConfiguration()
        {
            bool updated = false;

            if (!Enum.TryParse(Configuration.StartDay, true, out DayOfWeek _))
            {
                PrintWarning("Invalid StartDay in configuration. Defaulting to Friday.");
                Configuration.StartDay = "Friday";
                updated = true;
            }

            if (Configuration.StartHour < 0 || Configuration.StartHour > 23)
            {
                PrintWarning("Invalid StartHour in configuration. Defaulting to 14 (2 PM).");
                Configuration.StartHour = 14;
                updated = true;
            }

            if (updated)
            {
                SaveConfig(); // Save validated changes to the configuration file
            }
        }
     
        #endregion

        #region Permissions
        private const string AdminPermission = "rustroyale.admin";

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
            ValidateConfiguration(); // Ensures configuration integrity
            ScheduleTournament();
        }

        #region Data Logging
        private string DataDirectory => $"{Interface.Oxide.DataDirectory}/RustRoyale";
        private string CurrentTournamentFile => $"{DataDirectory}/Tournament_{DateTime.UtcNow:yyyyMMddHHmmss}.data";

        private void EnsureDataDirectory()
        {
            if (!Directory.Exists(DataDirectory))
            {
                Directory.CreateDirectory(DataDirectory);
            }
        }

        private void LogEvent(string message)
        {
            try
            {
                EnsureDataDirectory();
                File.AppendAllText(CurrentTournamentFile, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} - {message}\n");
            }
            catch (Exception ex)
            {
                PrintError($"Failed to log event: {ex.Message}");
            }
        }

        private void ManageOldTournamentFiles()
        {
            var files = Directory.GetFiles(DataDirectory, "Tournament_*.data");
            if (files.Length > 30)
            {
                Array.Sort(files);
                for (int i = 0; i < files.Length - 30; i++)
                {
                    File.Delete(files[i]);
                }
            }
        }

        private void LogParticipants()
        {
            EnsureDataDirectory();
            string participantsLog = $"Participants at tournament start: {string.Join(", ", participants)}";
            LogEvent(participantsLog);
        }

        #endregion

        #region Tournament Logic
        private readonly Dictionary<ulong, PlayerStats> playerStats = new Dictionary<ulong, PlayerStats>();
        private readonly HashSet<ulong> participants = new HashSet<ulong>();
        private readonly HashSet<ulong> openTournamentPlayers = new HashSet<ulong>();
        private bool isTournamentRunning = false;
        private DateTime tournamentStartTime;
        private DateTime tournamentEndTime;
        private Timer countdownTimer;

        private void ScheduleTournament()
        {
            if (!Configuration.AutoStartEnabled)
            {
                PrintWarning("AutoStart is disabled. Tournament will not start automatically.");
                return;
            }

            try
            {
                DayOfWeek startDay = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), Configuration.StartDay, true);
                int startHour = Configuration.StartHour;

                DateTime now = DateTime.UtcNow;
                DateTime nextStartDay = now.AddDays((7 + (int)startDay - (int)now.DayOfWeek) % 7).Date;
                tournamentStartTime = nextStartDay.AddHours(startHour);

                if (tournamentStartTime <= now)
                {
                    tournamentStartTime = tournamentStartTime.AddDays(7);
                }

                double secondsToStart = (tournamentStartTime - now).TotalSeconds;

                Puts($"Tournament scheduled to start on {tournamentStartTime:yyyy-MM-dd HH:mm:ss} (in {secondsToStart / 3600:F2} hours).");

                ScheduleDailyCountdown(tournamentStartTime);

                timer.Once((float)secondsToStart, StartTournament);
            }
            catch (Exception ex)
            {
                PrintError($"Error scheduling tournament: {ex.Message}");
            }
        }

        private void ScheduleDailyCountdown(DateTime tournamentStartTime)
        {
            DateTime now = DateTime.UtcNow;
            DateTime dailyNotificationTime = now.Date.AddHours(Configuration.StartHour);

            if (dailyNotificationTime <= now)
            {
                dailyNotificationTime = dailyNotificationTime.AddDays(1);
            }

            double secondsToFirstNotification = (dailyNotificationTime - now).TotalSeconds;

            countdownTimer = timer.Once((float)secondsToFirstNotification, () =>
            {
                NotifyTournamentCountdown(tournamentStartTime);

                countdownTimer = timer.Every(86400f, () =>
                {
                    if (isTournamentRunning)
                    {
                        timer.Destroy(ref countdownTimer);
                    }
                    else
                    {
                        NotifyTournamentCountdown(tournamentStartTime);
                    }
                });
            });

            Puts($"Daily countdown notifications scheduled to start at {dailyNotificationTime:yyyy-MM-dd HH:mm:ss} UTC.");
        }

        private void NotifyTournamentCountdown(DateTime tournamentStartTime)
        {
            DateTime now = DateTime.UtcNow;
            double daysToStart = (tournamentStartTime - now).TotalDays;

            if (daysToStart < 1) return;

            string message = $"The tournament will start in {Math.Ceiling(daysToStart)} day(s)!";
            Notify(message);
        }

        private void StartTournament()
        {
            if (isTournamentRunning)
            {
                TimeSpan remainingTime = tournamentEndTime - DateTime.UtcNow;
                Notify($"A tournament is already running! Time remaining: {remainingTime:hh\\:mm\\:ss}");
                return;
            }

            Puts("RustRoyale: Tournament started!");
            LogEvent($"Tournament started at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}. Duration: {Configuration.DurationHours} hours.");

            isTournamentRunning = true;
            tournamentEndTime = tournamentStartTime.AddHours(Configuration.DurationHours);

            playerStats.Clear();
            participants.UnionWith(openTournamentPlayers);
            LogParticipants(); // Log participants at the start

            Notify($"Tournament has started! Let the games begin! (Time remaining: {Configuration.DurationHours} hours)");
        }

        private void EndTournament()
        {
            if (!isTournamentRunning)
            {
                Notify("No tournament is currently running to end!");
                return;
            }

            Puts("RustRoyale: Tournament ended!");
            isTournamentRunning = false;

            AnnounceWinners();
            Notify("Tournament has ended! Winners are being announced.");
        }

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            if (!isTournamentRunning || victim == null) return;

            var attacker = info?.InitiatorPlayer;

            bool isVictimNPC = victim.ShortPrefabName.Contains("murderer") ||
                               victim.ShortPrefabName.Contains("zombie") ||
                               victim.ShortPrefabName.Contains("scientist") ||
                               victim.ShortPrefabName.Contains("scarecrow");

            bool isVictimEntity = victim.ShortPrefabName.Contains("helicopter") ||
                                  victim.ShortPrefabName.Contains("bradley");

            // Player kills an NPC
            if (isVictimNPC)
            {
                if (attacker != null && !attacker.IsNpc && participants.Contains(attacker.userID))
                {
                    UpdatePlayerScore(attacker.userID, "NPC", "killing an NPC");
                    string npcKillMessage = $"{attacker.displayName} received 1 point for killing an NPC. Total score: {playerStats[attacker.userID].Score}";
                    SendGlobalChatMessage(npcKillMessage);
                    SendDiscordMessage(npcKillMessage);
                }
                return;
            }

            // Player destroys a Helicopter or Bradley
            if (isVictimEntity)
            {
                if (attacker != null && !attacker.IsNpc && participants.Contains(attacker.userID))
                {
                    UpdatePlayerScore(attacker.userID, "ENT", "destroying a Helicopter or Bradley");
                    string entityKillMessage = $"{attacker.displayName} received 5 points for destroying a Helicopter or Bradley. Total score: {playerStats[attacker.userID].Score}";
                    SendGlobalChatMessage(entityKillMessage);
                    SendDiscordMessage(entityKillMessage);
                }
                return;
            }

            // Player is killed by an NPC
            if (attacker == null || attacker.IsNpc)
            {
                if (participants.Contains(victim.userID))
                {
                    UpdatePlayerScore(victim.userID, "JOKE", "being killed by an NPC");
                    string npcDeathMessage = $"{victim.displayName} lost 1 point for being killed by an NPC. Total score: {playerStats[victim.userID].Score}";
                    SendGlobalChatMessage(npcDeathMessage);
                    SendDiscordMessage(npcDeathMessage);
                }
                return;
            }

            // Player kills themselves (self-inflicted)
            if (attacker == victim)
            {
                if (participants.Contains(victim.userID))
                {
                    UpdatePlayerScore(victim.userID, "JOKE", "self-inflicted death");
                    string selfInflictedMessage = $"{victim.displayName} lost 1 point for self-inflicted death. Total score: {playerStats[victim.userID].Score}";
                    SendGlobalChatMessage(selfInflictedMessage);
                    SendDiscordMessage(selfInflictedMessage);
                }
                return;
            }

            // Player kills another player
            if (participants.Contains(attacker.userID) && participants.Contains(victim.userID))
            {
                UpdatePlayerScore(victim.userID, "DEAD", $"being killed by {attacker.displayName}");
                string victimDeathMessage = $"{victim.displayName} lost 3 points for being killed by {attacker.displayName}. Total score: {playerStats[victim.userID].Score}";
                SendGlobalChatMessage(victimDeathMessage);
                SendDiscordMessage(victimDeathMessage);

                UpdatePlayerScore(attacker.userID, "KILL", $"killing {victim.displayName}");
                string attackerKillMessage = $"{attacker.displayName} received 3 points for killing {victim.displayName}. Total score: {playerStats[attacker.userID].Score}";
                SendGlobalChatMessage(attackerKillMessage);
                SendDiscordMessage(attackerKillMessage);
            }
        }

        private void UpdatePlayerScore(ulong userId, string actionCode, string actionDescription)
        {
            if (!isTournamentRunning || !participants.Contains(userId)) return;

            if (!playerStats.ContainsKey(userId))
                playerStats[userId] = new PlayerStats(userId);

            if (Configuration.ScoreRules.TryGetValue(actionCode, out int points))
            {
                playerStats[userId].Score += points;
                string logMessage = $"{BasePlayer.FindByID(userId)?.displayName} received {points} point(s) for {actionDescription}. Total score: {playerStats[userId].Score}.";
                LogEvent(logMessage);
                Notify(logMessage);
            }
        }

        private void AnnounceWinners()
        {
            var sortedPlayers = new List<PlayerStats>(playerStats.Values);
            sortedPlayers.Sort((a, b) => b.Score.CompareTo(a.Score));

            string discordMessage = "Tournament Results:";
            foreach (var player in BasePlayer.activePlayerList)
            {
                player.ChatMessage("Tournament Results:");
                for (int i = 0; i < Math.Min(3, sortedPlayers.Count); i++)
                {
                    string message = $"{i + 1}. {sortedPlayers[i].Name} - {sortedPlayers[i].Score} Points";
                    player.ChatMessage(message);
                    discordMessage += $"\n{message}";
                }
            }

            SendDiscordMessage(discordMessage);
        }

        private void Notify(string message)
        {
            SendGlobalChatMessage(message);
            SendDiscordMessage(message);
            Puts(message);
        }
        #endregion

        private void DisplayTimeRemaining()
        {
            if (!isTournamentRunning)
            {
                SendGlobalChatMessage("No tournament is currently running.");
                return;
            }

            TimeSpan remainingTime = tournamentEndTime - DateTime.UtcNow;
            SendGlobalChatMessage($"Time remaining in the tournament: {remainingTime:hh\\:mm\\:ss}");
            SendDiscordMessage($"Time remaining in the tournament: {remainingTime:hh\\:mm\\:ss}");
        }

        private void DisplayScores()
        {
            if (playerStats.Count == 0)
            {
                SendGlobalChatMessage("No scores to display. Tournament might not have started yet.");
                return;
            }

            SendGlobalChatMessage("Tournament Scores:");
            foreach (var stats in playerStats.Values)
            {
                SendGlobalChatMessage($"{stats.Name}: {stats.Score} Points");
            }
        }
        #endregion

        #region Helpers
        private void SendGlobalChatMessage(string message)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                player.SendConsoleCommand("chat.add", Configuration.ChatIconSteamId, Configuration.ChatUsername, message);
            }
        }

        private void SendDiscordMessage(string content)
        {
            if (string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                Puts("Discord webhook URL is not configured.");
                return;
            }

            var payload = new Dictionary<string, string>
            {
                {"content", content}
            };

            string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

            webrequest.Enqueue(Configuration.DiscordWebhookUrl, jsonPayload, (code, response) =>
            {
                if (code != 204)
                {
                    PrintWarning($"Failed to send Discord message: {response}");
                }
            }, this, RequestMethod.POST, new Dictionary<string, string> { { "Content-Type", "application/json" } });
        }
        #endregion

        #region PlayerStats Class
        private class PlayerStats
        {
            public ulong UserId { get; }
            public string Name => BasePlayer.FindByID(UserId)?.displayName ?? "Unknown";
            public int Score { get; set; }

            public PlayerStats(ulong userId)
            {
                UserId = userId;
                Score = 0;
            }
        }
        #endregion

        #region Commands
        [ChatCommand("start_tournament")]
        private void StartTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                player.ChatMessage("You do not have permission to start the tournament.");
                return;
            }
            StartTournament();
        }

        [ChatCommand("end_tournament")]
        private void EndTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                player.ChatMessage("You do not have permission to end the tournament.");
                return;
            }
            EndTournament();
        }

        [ChatCommand("time_tournament")]
        private void TimeTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (!isTournamentRunning)
            {
                Notify("No tournament is currently running.");
            }
            else
            {
                TimeSpan remainingTime = tournamentEndTime - DateTime.UtcNow;
                player.ChatMessage($"Time remaining in the tournament: {remainingTime:hh\\:mm\\:ss}");
            }
        }

        [ChatCommand("enter_tournament")]
        private void EnterTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (isTournamentRunning && DateTime.UtcNow < tournamentStartTime.AddHours(6))
            {
                if (!participants.Contains(player.userID))
                {
                    participants.Add(player.userID);
                    player.ChatMessage("You have joined the tournament!");
                    LogEvent($"{player.displayName} joined the tournament.");
                }
                else
                {
                    player.ChatMessage("You are already in the tournament.");
                }
            }
            else
            {
                player.ChatMessage("Tournament is either not running or the joining period has ended.");
            }
        }

        [ChatCommand("exit_tournament")]
        private void ExitTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (isTournamentRunning && DateTime.UtcNow < tournamentStartTime.AddHours(6))
            {
                if (participants.Contains(player.userID))
                {
                    participants.Remove(player.userID);
                    player.ChatMessage("You have left the tournament.");
                    LogEvent($"{player.displayName} left the tournament.");
                }
                else
                {
                    player.ChatMessage("You are not in the tournament.");
                }
            }
            else
            {
                player.ChatMessage("Tournament is either not running or the leaving period has ended.");
            }
        }

        [ChatCommand("open_tournament")]
        private void OpenTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (openTournamentPlayers.Add(player.userID))
            {
                player.ChatMessage("You have opted in to automatically join all tournaments.");
                LogEvent($"{player.displayName} opted in to automatic tournament entry.");
            }
            else
            {
                player.ChatMessage("You are already opted in for automatic tournament entry.");
            }
        }

        [ChatCommand("close_tournament")]
        private void CloseTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (openTournamentPlayers.Remove(player.userID))
            {
                player.ChatMessage("You have opted out of automatic tournament entry.");
                LogEvent($"{player.displayName} opted out of automatic tournament entry.");
            }
            else
            {
                player.ChatMessage("You were not opted in for automatic tournament entry.");
            }
        }

        [ChatCommand("status_tournament")]
        private void StatusTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (isTournamentRunning)
            {
                TimeSpan remainingTime = tournamentEndTime - DateTime.UtcNow;
                player.ChatMessage($"The tournament is currently running! Time remaining: {remainingTime:hh\\:mm\\:ss}");
            }
            else
            {
                DateTime now = DateTime.UtcNow;
                DayOfWeek startDay = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), Configuration.StartDay, true);
                int startHour = Configuration.StartHour;

                DateTime nextStartDay = now.AddDays((7 + (int)startDay - (int)now.DayOfWeek) % 7).Date.AddHours(startHour);

                if (nextStartDay <= now)
                {
                    nextStartDay = nextStartDay.AddDays(7);
                }

                TimeSpan timeUntilStart = nextStartDay - now;
                player.ChatMessage($"The tournament is not currently running. It will start in {timeUntilStart.Days} day(s) and {timeUntilStart:hh\\:mm\\:ss}.");
            }
        }

        [ChatCommand("score_tournament")]
        private void ScoreTournamentCommand(BasePlayer player, string command, string[] args)
        {
            DisplayScores();
        }

        private bool HasAdminPermission(BasePlayer player)
        {
            return permission.UserHasPermission(player.UserIDString, AdminPermission);
        }
        #endregion
    }
}
