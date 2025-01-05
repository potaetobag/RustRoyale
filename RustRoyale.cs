using System;
using System.Collections.Generic;
using System.IO;
using Oxide.Core;
using Oxide.Core.Libraries;
using UnityEngine;
using System.Linq;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("RustRoyale", "Potaetobag", "1.0.2"), Description("Rust Royale custom tournament game mode with point-based scoring system.")]
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
            public int DataRetentionDays { get; set; } = 30; // Default to 30 days
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
            Puts("Creating default configuration...");
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

            if (Configuration.DataRetentionDays <= 0)
                {
                    PrintWarning("Invalid DataRetentionDays in configuration. Defaulting to 30 days.");
                    Configuration.DataRetentionDays = 30;
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
            LoadParticipantsData();
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
                Puts($"Created data directory: {DataDirectory}");
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
            try
            {
                var files = Directory.GetFiles(DataDirectory, "Tournament_*.data");
                DateTime cutoffDate = DateTime.UtcNow.AddDays(-Configuration.DataRetentionDays);

                foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            if (fileInfo.LastWriteTimeUtc < cutoffDate)
            {
                try
                {
                    File.Delete(file);
                    LogEvent($"Deleted old tournament file: {file}");
                }
                catch (Exception ex)
                {
                    PrintError($"Failed to delete file {file}: {ex.Message}");
                }
            }
        }
            }
            catch (Exception ex)
            {
                PrintError($"Failed to manage old tournament files: {ex.Message}");
            }
        }

        private void LogParticipants()
        {
            EnsureDataDirectory();
            string participantsLog = $"Participants at tournament start: {string.Join(", ", participantsData.Values.Select(p => p.Name))}";
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

        private bool IsWithinCutoffPeriod()
        {
            return isTournamentRunning && DateTime.UtcNow < tournamentStartTime.AddHours(6);
        }

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
                    if (!isTournamentRunning)
                    {
                        NotifyTournamentCountdown(tournamentStartTime);
                    }
                    else
                    {
                        timer.Destroy(ref countdownTimer);
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
                Notify("A tournament is already running!");
                return;
            }

            Puts("RustRoyale: Tournament started!");
            isTournamentRunning = true;
            tournamentEndTime = DateTime.UtcNow.AddHours(Configuration.DurationHours);

            playerStats.Clear();
            foreach (var participant in participantsData.Values)
        {
            participant.Score = 0; // Reset scores for the new tournament
            Notify($"{participant.Name} - {participant.Score}");
        }

        Notify($"Tournament has started! Duration: {Configuration.DurationHours} hours. Good luck to all participants!");

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

            Notify("Tournament has ended! Final Scores:");
            foreach (var participant in participantsData.Values.OrderByDescending(p => p.Score))
            {
                Notify($"{participant.Name}: {participant.Score} Points");
            }

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
                    Notify(npcKillMessage);
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
                    Notify(entityKillMessage);
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
                    Notify(npcDeathMessage);
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
                    Notify(selfInflictedMessage);
                }
                return;
            }

            // Player kills another player
            if (participants.Contains(attacker.userID) && participants.Contains(victim.userID))
            {
                UpdatePlayerScore(victim.userID, "DEAD", $"being killed by {attacker.displayName}");
                string victimDeathMessage = $"{victim.displayName} lost 3 points for being killed by {attacker.displayName}. Total score: {playerStats[victim.userID].Score}";
                Notify(victimDeathMessage);

                UpdatePlayerScore(attacker.userID, "KILL", $"killing {victim.displayName}");
                string attackerKillMessage = $"{attacker.displayName} received 3 points for killing {victim.displayName}. Total score: {playerStats[attacker.userID].Score}";
                Notify(attackerKillMessage);
            }
        }

        private void UpdatePlayerScore(ulong userId, string actionCode, string actionDescription)
        {
            // Ensure the participant exists in the tournament
            if (!participantsData.TryGetValue(userId, out var participant))
            {
                PrintWarning($"Player with UserID {userId} not found in participants.");
                return;
            }

            // Fetch the score adjustment from configuration
            if (!Configuration.ScoreRules.TryGetValue(actionCode, out int points))
            {
                PrintWarning($"Invalid action code: {actionCode}. No score adjustment made.");
                return;
            }

            // Update the participant's score
            participant.Score += points;

            // Save the updated participants data
            SaveParticipantsData();

            // Log the score update
            string logMessage = $"{participant.Name} received {points} point(s) for {actionDescription}. Total score: {participant.Score}.";
            LogEvent(logMessage);

            // Notify the player of their updated score
            var player = BasePlayer.FindByID(userId);
            player?.ChatMessage(logMessage);
        }

        private string HistoryFile => $"{DataDirectory}/TournamentHistory.json";

        private void SaveTournamentHistory()
        {
            var completedTournament = new
            {
                Date = DateTime.UtcNow,
                Winners = participantsData.Values.OrderByDescending(p => p.Score).Take(3).Select(p => new { p.Name, p.Score }).ToList(),
                Participants = participantsData
            };

            var history = new List<object>();
            if (File.Exists(HistoryFile))
            {
                history = JsonConvert.DeserializeObject<List<object>>(File.ReadAllText(HistoryFile)) ?? new List<object>();
            }
            history.Add(completedTournament);
            File.WriteAllText(HistoryFile, JsonConvert.SerializeObject(history, Formatting.Indented));
        }

        private string GetPlayerName(ulong userId)
        {
            if (BasePlayer.FindByID(userId) is BasePlayer player)
                return player.displayName;

            return participantsData.TryGetValue(userId, out var participant) ? participant.Name : "Unknown";
        }

        private string ParticipantsFile => $"{DataDirectory}/Participants.json";
        private Dictionary<ulong, PlayerStats> participantsData = new Dictionary<ulong, PlayerStats>();

        private void LoadParticipantsData()
        {
            try
            {
                EnsureDataDirectory(); // Ensure the directory exists
                if (File.Exists(ParticipantsFile))
                {
                    participantsData = JsonConvert.DeserializeObject<Dictionary<ulong, PlayerStats>>(File.ReadAllText(ParticipantsFile))
                               ?? new Dictionary<ulong, PlayerStats>();
                }
                else
                {
                    participantsData = new Dictionary<ulong, PlayerStats>();
                    SaveParticipantsData(); // Create the initial file if it doesn't exist
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to load participants data: {ex.Message}. Initializing with an empty dictionary.");
                participantsData = new Dictionary<ulong, PlayerStats>();
                SaveParticipantsData();
            }
        }

        private void SaveParticipantsData()
        {
            try
            {
                EnsureDataDirectory(); // Ensure the directory exists
                File.WriteAllText(ParticipantsFile, JsonConvert.SerializeObject(participantsData, Formatting.Indented));
            }
            catch (Exception ex)
            {
                PrintError($"Failed to save participants data: {ex.Message}");
            }
        }

        private bool ValidateAdminCommand(BasePlayer player, string actionName)
        {
            if (!HasAdminPermission(player))
            {
                player.ChatMessage($"You do not have permission to {actionName}.");
                return false;
            }
            return true;
        }

        private void AnnounceWinners()
        {
            Notify("Tournament Results:");
            var winners = participantsData.Values
                .OrderByDescending(participant => participant.Score)
                .Take(3)
                .ToList();

            for (int i = 0; i < winners.Count; i++)
            {
                Notify($"{i + 1}. {winners[i].Name} - {winners[i].Score} Points");
            }

            Puts($"Announced winners: {string.Join(", ", winners.Select(w => w.Name))}");
        }

        private void Notify(string message)
        {
            SendGlobalChatMessage(message);
            SendDiscordMessage(message);
            Puts(message);
        }
        #endregion

        private string GetTimeRemainingMessage()
        {
            if (!isTournamentRunning)
                return "No tournament is currently running.";

            TimeSpan remainingTime = tournamentEndTime - DateTime.UtcNow;
            return $"Time remaining in the tournament: {remainingTime:hh\\:mm\\:ss}";
        }

        private void DisplayTimeRemaining()
        {
            string message = GetTimeRemainingMessage();
            Notify(message); // Notify handles both chat and Discord.
        }

        private void DisplayScores()
        {
            if (participantsData.Count == 0)
            {
                SendGlobalChatMessage("No scores to display. Tournament might not have started yet.");
                return;
            }

            SendGlobalChatMessage("Tournament Scores:");
            foreach (var participant in participantsData.Values)
            {
                SendGlobalChatMessage($"{participant.Name}: {participant.Score} Points");
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

            var payload = new Dictionary<string, string> { { "content", content } };
            string jsonPayload = JsonConvert.SerializeObject(payload);

            webrequest.Enqueue(Configuration.DiscordWebhookUrl, jsonPayload, (code, response) =>
            {
                if (code != 204)
                {
                    PrintWarning($"Failed to send Discord message: {response}. HTTP Code: {code}");
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
            if (!ValidateAdminCommand(player, "start the tournament"))
            {
                return;
            }
            StartTournament();
        }

        [ChatCommand("end_tournament")]
        private void EndTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (!ValidateAdminCommand(player, "end the tournament"))
            {
                return;
            }
            EndTournament();
        }

        [ChatCommand("time_tournament")]
        private void TimeTournamentCommand(BasePlayer player, string command, string[] args)
        {
            string message = GetTimeRemainingMessage();
            player.ChatMessage(message); // Send only to the command initiator.
        }

        [ChatCommand("enter_tournament")]
        private void EnterTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (!isTournamentRunning)
            {
                player.ChatMessage("No tournament is currently running.");
                return;
            }

            if (!participantsData.ContainsKey(player.userID))
            {
                participantsData[player.userID] = new PlayerStats(player.userID);
                SaveParticipantsData();
                player.ChatMessage("You have successfully joined the tournament!");
            }
            else
            {
                player.ChatMessage("You are already participating in the tournament.");
            }
        }

        [ChatCommand("exit_tournament")]
        private void ExitTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (participantsData.Remove(player.userID))
            {
                SaveParticipantsData();
                player.ChatMessage("You have successfully left the tournament.");
            }
            else
            {
                player.ChatMessage("You are not part of the tournament.");
            }
        }

        [ChatCommand("open_tournament")]
        private void OpenTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (participantsData.ContainsKey(player.userID))
            {
                player.ChatMessage("You are already opted in to the tournament.");
                return;
            }

            participantsData[player.userID] = new PlayerStats(player.userID);

            SaveParticipantsData();

            player.ChatMessage("You have successfully opted in to the tournament!");
            LogEvent($"{player.displayName} opted in to the tournament.");
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
                player.ChatMessage($"Tournament is running! Time remaining: {remainingTime:hh\\:mm\\:ss}");

                // Display all participants and their scores
                if (participantsData.Count > 0)
                {
                    player.ChatMessage("Current Participants and Scores:");
                    foreach (var participant in participantsData.Values.OrderByDescending(p => p.Score))
                    {
                        player.ChatMessage($"{participant.Name}: {participant.Score} Points");
                    }
                }
                else
                {
                    player.ChatMessage("No participants have joined the tournament yet.");
                }

                // Show the player's own score
                if (participantsData.TryGetValue(player.userID, out var participantData))
                {
                    player.ChatMessage($"Your current score: {participantData.Score} Points");
                }
                else
                {
                    player.ChatMessage("You are not participating in this tournament.");
                }
            }
            else if (tournamentStartTime > DateTime.UtcNow)
            {
                TimeSpan timeUntilStart = tournamentStartTime - DateTime.UtcNow;
                player.ChatMessage($"Tournament not running. Next start in {timeUntilStart.Days} day(s) and {timeUntilStart:hh\\:mm\\:ss}.");
            }
            else
            {
                player.ChatMessage("No tournament is currently running or scheduled.");
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
