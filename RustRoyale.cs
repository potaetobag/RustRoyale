using System;
using System.Collections.Generic;
using System.IO;
using Oxide.Core;
using Oxide.Core.Libraries;
using UnityEngine;
using System.Linq;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace Oxide.Plugins
{
    [Info("RustRoyale", "Potaetobag", "1.0.3"), Description("Rust Royale custom tournament game mode with point-based scoring system.")]
    class RustRoyale : RustPlugin
    {
        #region Configuration
        private ConfigData Configuration;

        private class ConfigData
        {
            public string DiscordWebhookUrl { get; set; } = "";
            public string ChatIconSteamId { get; set; } = "76561199815164411";
            public string ChatUsername { get; set; } = "[RustRoyale]";
            public bool AutoStartEnabled { get; set; } = true;
            public string StartDay { get; set; } = "Friday";
            public int StartHour { get; set; } = 14;
            public int DurationHours { get; set; } = 72;
            public int DataRetentionDays { get; set; } = 30; // Default to 30 days
            public int TopPlayersToTrack { get; set; } = 3; // Default to Top 3 players
            public List<int> NotificationIntervals { get; set; } = new List<int> { 600, 60 }; // Default: every 10 minutes (600 seconds) and last minute (60 seconds)
            public Dictionary<string, int> ScoreRules { get; set; } = new Dictionary<string, int>
            {
                {"KILL", 3},     // Human player kills another human player
                {"DEAD", -3},    // Human player is killed by another human player
                {"JOKE", -1},    // Death by traps, NPCs, self-inflicted damage
                {"NPC", 1},      // Kill an NPC (Murderer, Zombie, Scientist, Scarecrow)
                {"ENT", 5}       // Kill a Helicopter or Bradley Tank
            };
            public Dictionary<string, string> MessageTemplates { get; set; } = new Dictionary<string, string>
            {
                {"StartTournament", "The tournament has started! Good luck to all participants! Time left: {TimeRemaining}."},
                {"EndTournament", "The tournament has ended! Congratulations to the top players!"},
                {"PlayerScoreUpdate", "{PlayerName} earned {Score} point{PluralS} for {Action}."},
                {"TopPlayers", "Top {Count} players: {PlayerList}."},
                {"TimeRemaining", "Time remaining in the tournament: {Time}."},
                {"JoinTournament", "{PlayerName} has joined the tournament!"},
                {"LeaveTournament", "{PlayerName} has left the tournament."},
                {"KillNPC", "{PlayerName} earned {Score} point{PluralS} for killing an NPC! Total score: {TotalScore}."},
                {"KillEntity", "{PlayerName} earned {Score} point{PluralS} for destroying a {EntityType}! Total score: {TotalScore}."},
                {"KillPlayer", "{PlayerName} earned {Score} point{PluralS} for killing {VictimName}! Total score: {TotalScore}."},
                {"SelfInflictedDeath", "{PlayerName} lost {Score} point{PluralS} for a self-inflicted death. Total score: {TotalScore}."},
                {"DeathByNPC", "{PlayerName} lost {Score} point{PluralS} for being killed by an NPC. Total score: {TotalScore}."},
                {"NoTournamentRunning", "No tournament is currently running."},
                {"ParticipantsAndScores", "Participants and Scores (Page {Page}/{TotalPages}): {PlayerList}."}
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

            if (Configuration.TopPlayersToTrack <= 0)
            {
                PrintWarning("Invalid TopPlayersToTrack in configuration. Defaulting to 3.");
                Configuration.TopPlayersToTrack = 3;
                updated = true;
            }

            // Validate NotificationIntervals
            if (Configuration.NotificationIntervals == null || !Configuration.NotificationIntervals.Any())
            {
                PrintWarning("NotificationIntervals is invalid or missing. Defaulting to every 10 minutes and the last minute.");
                Configuration.NotificationIntervals = new List<int> { 600, 60 }; // Default intervals
                updated = true;
            }

            if (Configuration.NotificationIntervals.Any(interval => interval <= 0))
            {
                PrintWarning("NotificationIntervals contains invalid values. Removing non-positive intervals.");
                Configuration.NotificationIntervals = Configuration.NotificationIntervals.Where(interval => interval > 0).ToList();
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
                LogMessage($"Created data directory: {DataDirectory}");
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
            PrintError($"Unexpected error: {ex.Message}\n{ex.StackTrace}");
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

        private void ScheduleCountdown(DateTime targetTime, Action onCompletion)
        {
            TimeSpan timeUntilTarget = targetTime - DateTime.UtcNow;

            if (timeUntilTarget.TotalSeconds <= 0)
            {
                onCompletion?.Invoke();
                return;
            }

            if (countdownTimer != null)
            {
                countdownTimer.Destroy();
                countdownTimer = null;
            }

            countdownTimer = timer.Repeat(1f, (int)timeUntilTarget.TotalSeconds, () =>
            {
                TimeSpan remainingTime = targetTime - DateTime.UtcNow;

                if (remainingTime.TotalSeconds <= 0)
                {
                    countdownTimer?.Destroy();
                    countdownTimer = null;
                    onCompletion?.Invoke();
                    return;
                }

                NotifyTournamentCountdown(remainingTime);
            });
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
                DateTime nextStartDay = now.AddDays((7 + (int)startDay - (int)now.DayOfWeek) % 7).Date.AddHours(startHour);

                if (nextStartDay <= now)
                {
                    nextStartDay = nextStartDay.AddDays(7); // Move to the next week
                }

                tournamentStartTime = nextStartDay;

                LogMessage($"Tournament scheduled to start on {tournamentStartTime:yyyy-MM-dd HH:mm:ss} UTC.");

                // Use the consolidated countdown logic
                ScheduleCountdown(tournamentStartTime, StartTournament);
            }
            catch (Exception ex)
            {
                PrintError($"Error scheduling tournament: {ex.Message}");
            }
        }

        private void ScheduleDailyCountdown()
        {
            DateTime now = DateTime.UtcNow;
            DateTime dailyNotificationTime = now.Date.AddHours(Configuration.StartHour);

            if (dailyNotificationTime <= now)
            {
                dailyNotificationTime = dailyNotificationTime.AddDays(1);
            }

            Puts($"Daily countdown notifications scheduled to start at {dailyNotificationTime:yyyy-MM-dd HH:mm:ss} UTC.");

            ScheduleCountdown(dailyNotificationTime, () =>
            {
                NotifyTournamentCountdown(dailyNotificationTime - DateTime.UtcNow);
                ScheduleDailyCountdown(); // Reschedule the daily countdown for the next day
            });
        }

        private int lastNotifiedMinutes = -1;

        private void NotifyTournamentCountdown(TimeSpan remainingTime)
        {
            int currentSeconds = (int)remainingTime.TotalSeconds;

            if (Configuration.NotificationIntervals.Contains(currentSeconds) && lastNotifiedMinutes != currentSeconds)
            {
                lastNotifiedMinutes = currentSeconds;

                Notify("TimeRemaining", null, placeholders: new Dictionary<string, string>
                {
                    {"Time", FormatTimeRemaining(remainingTime)}
                });
            }
        }

        private bool ShouldSendNotification(TimeSpan remainingTime)
        {
            // Notify every 10 minutes or in the last minute
            return remainingTime.TotalMinutes % 10 == 0 || remainingTime.TotalSeconds <= 60;
        }

        private void StartTournament()
        {
            try
            {
                if (isTournamentRunning)
                {
                    Notify("StartTournament", null);
                    return;
                }

                if (Configuration.DurationHours <= 0)
                {
                    PrintError("Invalid tournament duration. Ensure `DurationHours` is greater than 0.");
                    return;
                }

                Puts("RustRoyale: Tournament started!");
                isTournamentRunning = true;
                tournamentEndTime = DateTime.UtcNow.AddHours(Configuration.DurationHours);

                playerStats.Clear();
                foreach (var participant in participantsData.Values)
                {
                    participant.Score = 0;
                }

                if (countdownTimer != null)
                {
                    countdownTimer.Destroy();
                    countdownTimer = null;
                }

                Notify("StartTournament", null, placeholders: new Dictionary<string, string>
                {
                    {"TimeRemaining", FormatTimeRemaining(tournamentEndTime - DateTime.UtcNow)},
                    {"Duration", Configuration.DurationHours.ToString()}
                });

                LogEvent("Tournament started successfully.");
            }
            catch (Exception ex)
            {
                PrintError($"Failed to start tournament: {ex.Message}");
            }
        }

        private void EndTournament()
        {
            if (!isTournamentRunning)
            {
                Notify("EndTournament", null);
                return;
            }

            Puts("RustRoyale: Tournament ended!");
            isTournamentRunning = false;

            var sortedParticipants = participantsData.Values.AsEnumerable()
            .OrderByDescending(p => p.Score)
            .Take(Configuration.TopPlayersToTrack)
            .ToList();

            foreach (var participant in sortedParticipants)
            {
                Notify("PlayerScoreUpdate", null, score: participant.Score, action: "final score");
            }

            Notify("EndTournament", null, action: "Final scores announced!");
        }

        private void OnPlayerInit(BasePlayer player)
        {
            if (player != null && !string.IsNullOrEmpty(player.displayName))
            {
                playerNameCache[player.userID] = player.displayName;
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player != null)
            {
                playerNameCache.TryRemove(player.userID, out _);
            }
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
            if (isVictimNPC && attacker != null && !attacker.IsNpc && participants.Contains(attacker.userID))
            {
                UpdatePlayerScore(attacker.userID, "NPC", "killing an NPC");
                Notify("KillNPC", attacker, score: Configuration.ScoreRules["NPC"]);
                return;
            }

            // Player destroys a Helicopter or Bradley
            if (isVictimEntity && attacker != null && !attacker.IsNpc && participants.Contains(attacker.userID))
            {
                UpdatePlayerScore(attacker.userID, "ENT", "destroying a Helicopter or Bradley");
                Notify("KillEntity", attacker, score: Configuration.ScoreRules["ENT"], entityType: victim.ShortPrefabName);
                return;
            }

            // Player is killed by an NPC
            if ((attacker == null || attacker.IsNpc) && participants.Contains(victim.userID))
            {
                UpdatePlayerScore(victim.userID, "JOKE", "being killed by an NPC");
                Notify("DeathByNPC", victim, score: Configuration.ScoreRules["JOKE"]);
                return;
            }

            // Player kills themselves (self-inflicted)
            if (attacker == victim && participants.Contains(victim.userID))
            {
                UpdatePlayerScore(victim.userID, "JOKE", "self-inflicted death");
                Notify("SelfInflictedDeath", victim, score: Configuration.ScoreRules["JOKE"]);
                return;
            }

            // Player kills another player
            if (attacker != null && participants.Contains(attacker.userID) && participants.Contains(victim.userID))
            {
                UpdatePlayerScore(victim.userID, "DEAD", $"being killed by {attacker.displayName}");
                Notify("PlayerScoreUpdate", victim, score: Configuration.ScoreRules["DEAD"], action: $"being killed by {attacker.displayName}");

                UpdatePlayerScore(attacker.userID, "KILL", $"killing {victim.displayName}");
                Notify("KillPlayer", attacker, score: Configuration.ScoreRules["KILL"], victimName: victim.displayName);
            }
        }

        private void UpdatePlayerScore(ulong userId, string actionCode, string actionDescription)
        {
            if (!participantsData.TryGetValue(userId, out var participant))
            {
                PrintWarning($"Player with UserID {userId} not found in participants.");
                return;
            }

            if (!Configuration.ScoreRules.TryGetValue(actionCode, out int points))
            {
                PrintWarning($"Invalid action code: {actionCode}. No score adjustment made.");
                return;
            }

            participant.Score += points;
            SaveParticipantsData();

            Notify("PlayerScoreUpdate", BasePlayer.FindByID(userId), score: points, action: actionDescription);
            LogEvent($"{participant.Name} received {points} point(s) for {actionDescription}. Total score: {participant.Score}.");
        }

        private void OnUnload()
        {
            // Ensure all timers are cleaned up
            CleanupTimers();

            // Handle any remaining saveDataTimer explicitly
            if (saveDataTimer != null)
            {
                saveDataTimer.Destroy();
                saveDataTimer = null;
            }

            // Perform the final save of participants data
            SaveParticipantsData();
        }

        private void CleanupTimers()
        {
            if (saveDataTimer != null)
            {
                saveDataTimer.Destroy();
                saveDataTimer = null;
            }
            if (countdownTimer != null)
            {
                countdownTimer.Destroy();
                countdownTimer = null;
            }
        }

        private string HistoryFile => $"{DataDirectory}/TournamentHistory.json";

        private void SaveTournamentHistory(List<PlayerStats> topPlayers)
        {
            var completedTournament = new
            {
                Date = DateTime.UtcNow,
                Winners = topPlayers.Select(p => new { p.Name, p.Score }).ToList(),
                Participants = participantsData.Values.Select(p => new { p.Name, p.Score }).ToList()
            };

            // Save to TournamentHistory.json
            var history = new List<object>();
            if (File.Exists(HistoryFile))
            {
                history = JsonConvert.DeserializeObject<List<object>>(File.ReadAllText(HistoryFile)) ?? new List<object>();
            }
            history.Add(completedTournament);
            File.WriteAllText(HistoryFile, JsonConvert.SerializeObject(history, Formatting.Indented));

            // Save winners to a separate file
            string winnersFile = $"{DataDirectory}/Winners_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            File.WriteAllText(winnersFile, JsonConvert.SerializeObject(completedTournament.Winners, Formatting.Indented));

        }

        private void LogMessage(string message, bool logToDiscord = false)
        {
            Puts(message);

            if (logToDiscord && !string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                SendDiscordMessage(message);
            }
        }

        private ConcurrentDictionary<ulong, string> playerNameCache = new ConcurrentDictionary<ulong, string>();

        private string GetPlayerName(ulong userId)
        {
            if (playerNameCache.TryGetValue(userId, out var cachedName))
            {
                return cachedName;
            }

            var player = BasePlayer.FindByID(userId);
            if (player != null && !string.IsNullOrEmpty(player.displayName))
            {
                playerNameCache[userId] = player.displayName;
                return player.displayName;
            }

            if (participantsData.TryGetValue(userId, out var participant) && !string.IsNullOrEmpty(participant.Name))
            {
                playerNameCache[userId] = participant.Name;
                return participant.Name;
            }

            return "Unknown";
        }

        private string ParticipantsFile => $"{DataDirectory}/Participants.json";
        
        private ConcurrentDictionary<ulong, PlayerStats> participantsData = new ConcurrentDictionary<ulong, PlayerStats>();

        private void LoadParticipantsData()
        {
            try
            {
                EnsureDataDirectory(); // Ensure the directory exists
                if (File.Exists(ParticipantsFile))
                {
                    participantsData = new ConcurrentDictionary<ulong, PlayerStats>(
                        JsonConvert.DeserializeObject<Dictionary<ulong, PlayerStats>>(File.ReadAllText(ParticipantsFile))
                        ?? new Dictionary<ulong, PlayerStats>()
                    );

                    // Populate the cache only for valid participant names
                    foreach (var participant in participantsData.Values)
                    {
                        if (!string.IsNullOrEmpty(participant.Name))
                        {
                            playerNameCache[participant.UserId] = participant.Name;
                        }
                    }
                }
                else
                {
                    participantsData = new ConcurrentDictionary<ulong, PlayerStats>();
                    PrintWarning("Participants data file is missing or corrupt. Starting with an empty dataset.");
                    SaveParticipantsData(); // Create the initial file if it doesn't exist
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to load participants data: {ex.Message}. Initializing with an empty dictionary.");
                participantsData = new ConcurrentDictionary<ulong, PlayerStats>();
                SaveParticipantsData();
            }
        }

        private Timer saveDataTimer;

        // Call this during Init to start periodic saving
        private void StartDataSaveTimer()
        {
            saveDataTimer = timer.Every(300f, SaveParticipantsData); // Save every 5 minutes
        }

        private readonly object participantsDataLock = new object();

        private void SaveParticipantsData()
        {
            try
            {
                EnsureDataDirectory(); // Ensure the directory exists
                string serializedData;

                lock (participantsDataLock) // Thread-safe access
                {
                    serializedData = SerializeParticipantsData(participantsData);
                }

                File.WriteAllText(ParticipantsFile, serializedData);
            }
            catch (IOException ioEx)
            {
                PrintError($"IO error while saving participants data: {ioEx.Message}");
            }
            catch (JsonException jsonEx)
            {
                PrintError($"Serialization error while saving participants data: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                PrintError($"Unexpected error while saving participants data: {ex.Message}");
            }
        }

        private string SerializeParticipantsData(ConcurrentDictionary<ulong, PlayerStats> participants)
        {
            try
            {
                return JsonConvert.SerializeObject(participants.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), Formatting.Indented);
            }
            catch (JsonException ex)
            {
                PrintError($"Error serializing participants data: {ex.Message}");
                throw; // Re-throw to allow the caller to handle this
            }
        }

        private string FormatTimeRemaining(TimeSpan timeSpan)
        {
            // Ensure no negative values in case time has elapsed
            timeSpan = timeSpan < TimeSpan.Zero ? TimeSpan.Zero : timeSpan;

            // Format as HH:MM:SS
            return $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
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
            Notify("Tournament Results", null); // Pass `null` for the `player` argument
            var winners = participantsData.Values
                .OrderByDescending(participant => participant.Score)
                .Take(Configuration.TopPlayersToTrack)
                .ToList();

            var playerList = string.Join(", ", winners.Select((p, i) => $"{i + 1}. {p.Name} ({p.Score} Points)"));

            Notify("TopPlayers", null, 0, "announcing winners", "", "", new Dictionary<string, string>
            {
                {"Count", winners.Count.ToString()},
                {"PlayerList", playerList}
            });

            LogMessage($"Announced winners: {string.Join(", ", winners.Select(w => w.Name))}");
        }

        private void Notify(string templateName, BasePlayer player, int score = 0, string action = "", string entityType = "", string victimName = "", Dictionary<string, string> placeholders = null)
        {
            if (!Configuration.MessageTemplates.TryGetValue(templateName, out var template))
            {
                PrintWarning($"Template '{templateName}' not found in configuration.");
                return;
            }

            placeholders ??= new Dictionary<string, string>
            {
                ["PlayerName"] = player?.displayName ?? "Unknown",
                ["VictimName"] = victimName,
                ["Score"] = Math.Abs(score).ToString(),
                ["TotalScore"] = participantsData.TryGetValue(player?.userID ?? 0, out var participant) ? participant.Score.ToString() : "0",
                ["EntityType"] = entityType,
                ["Action"] = action,
                ["PluralS"] = Math.Abs(score) == 1 ? "" : "s"
            };

            // Extract placeholders from the template
            var templatePlaceholders = GetPlaceholdersFromTemplate(template);

            // Validate that all placeholders in the template are provided
            var missingPlaceholders = templatePlaceholders.Except(placeholders.Keys).ToList();
            if (missingPlaceholders.Any())
            {
                PrintWarning($"Template '{templateName}' is missing placeholders: {string.Join(", ", missingPlaceholders)}.");
                return;
            }

            // Replace placeholders in the template
            foreach (var placeholder in placeholders)
            {
                if (!template.Contains($"{{{placeholder.Key}}}"))
                {
                    PrintWarning($"Placeholder '{placeholder.Key}' is not used in template '{templateName}'.");
                }
                template = template.Replace($"{{{placeholder.Key}}}", placeholder.Value);
            }

            SendTournamentMessage(template);
            SendDiscordMessage(template);
            Puts(template);
        }

        private IEnumerable<string> GetPlaceholdersFromTemplate(string template)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(template, @"\{(\w+)\}");
            return matches.Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value);
        }

        private string FormatMessage(string template, Dictionary<string, string> placeholders)
        {
            foreach (var placeholder in placeholders)
            {
                template = template.Replace($"{{{placeholder.Key}}}", placeholder.Value);
            }
            return template;
        }

        private void ValidateMessageTemplates()
        {
            var defaultTemplates = new Dictionary<string, string>
            {
                {"StartTournament", "The tournament has started! Good luck to all participants! Time left: {TimeRemaining}."},
                {"EndTournament", "The tournament has ended! Congratulations to the top players!"},
                {"PlayerScoreUpdate", "{PlayerName} earned {Score} point{PluralS} for {Action}."},
                {"TopPlayers", "Top {Count} players: {PlayerList}."},
                {"TimeRemaining", "Time remaining in the tournament: {Time}."}
            };

            var missingTemplates = new List<string>();
            var missingPlaceholders = new Dictionary<string, List<string>>();

            foreach (var templateKey in defaultTemplates.Keys)
            {
                if (!Configuration.MessageTemplates.TryGetValue(templateKey, out string message))
                {
                    missingTemplates.Add(templateKey);
                    Configuration.MessageTemplates[templateKey] = defaultTemplates[templateKey];
                    continue;
                }

                // Validate placeholders
                var requiredPlaceholders = defaultTemplates[templateKey]
                    .Split(new[] { '{', '}' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where((s, i) => i % 2 == 1) // Get only placeholder names
                    .ToList();

                var placeholdersNotFound = requiredPlaceholders
                    .Where(placeholder => !message.Contains($"{{{placeholder}}}"))
                    .ToList();

                if (placeholdersNotFound.Any())
                {
                    missingPlaceholders[templateKey] = placeholdersNotFound;
                }
            }

            foreach (var missingTemplate in missingTemplates)
            {
                PrintWarning($"Template '{missingTemplate}' was missing and has been added with default.");
            }

            foreach (var missing in missingPlaceholders)
            {
                PrintWarning($"Template '{missing.Key}' is missing placeholders: {string.Join(", ", missing.Value)}");
            }

            if (!missingTemplates.Any() && !missingPlaceholders.Any())
            {
                Puts("All templates validated successfully.");
            }
            else
            {
                PrintWarning("Template validation completed with issues. Review the warnings.");
            }

            SaveConfig();
        }

        #endregion

        private string GetTimeRemainingMessage()
        {
            if (!isTournamentRunning)
                return "No tournament is currently running.";

            TimeSpan remainingTime = tournamentEndTime - DateTime.UtcNow;
            string formattedTime = FormatTimeRemaining(remainingTime);
            return $"Time remaining in the tournament: {formattedTime}";
        }

        private void DisplayTimeRemaining()
        {
            string message = GetTimeRemainingMessage();
            Notify("TimeRemaining", null, placeholders: new Dictionary<string, string>
            {
                {"Time", message}
            });
        }

        private void DisplayScores()
        {
            if (participantsData.Count == 0)
            {
                Notify("NoScores", null);
                return;
            }

            var scoreList = string.Join(", ", participantsData.Values
                .OrderByDescending(p => p.Score)
                .Select(p => $"{p.Name} ({p.Score} Points)"));

            Notify("TournamentScores", null, action: "displaying scores", victimName: "", entityType: "", score: 0, placeholders: new Dictionary<string, string>
            {
                {"PlayerList", scoreList}
            });
        }

        #endregion

        #region Helpers
        private void SendTournamentMessage(string message, HashSet<ulong> targetPlayers = null)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                // Send to specific target players if provided, otherwise to all participants
                if ((targetPlayers == null && participantsData.ContainsKey(player.userID)) || 
                    (targetPlayers != null && targetPlayers.Contains(player.userID)))
                {
                    player.SendConsoleCommand("chat.add", Configuration.ChatIconSteamId, Configuration.ChatUsername, message);
                }
            }
        }

        private void SendDiscordMessage(string content)
        {
            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                var payload = new Dictionary<string, string> { { "content", content } };
                string jsonPayload = JsonConvert.SerializeObject(payload);

                webrequest.Enqueue(Configuration.DiscordWebhookUrl, jsonPayload, (code, response) =>
                {
                    if (code != 204)
                    {
                        PrintWarning($"Failed to send Discord message: {response}. HTTP Code: {code}");
                        LogMessage($"Discord notification failed: {content}");
                    }
                }, this, RequestMethod.POST, new Dictionary<string, string> { { "Content-Type", "application/json" } });
            }
            else
            {
                LogMessage($"Discord notification skipped (not configured): {content}");
            }
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

            if (participantsData.TryAdd(player.userID, new PlayerStats(player.userID)))
            {
                SaveParticipantsData();
                LogEvent($"{player.displayName} joined the tournament.");
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
            if (participantsData.TryRemove(player.userID, out _))
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
            int page = 1; // Default page
            int pageSize = 10; // Number of participants per page

            if (args.Length > 0 && int.TryParse(args[0], out int parsedPage))
            {
                page = Math.Max(1, parsedPage); // Ensure the page is at least 1
            }

            if (isTournamentRunning)
            {
                string timeRemainingMessage = GetTimeRemainingMessage();
                player.ChatMessage(timeRemainingMessage);

                var participants = participantsData.Values.OrderByDescending(p => p.Score).ToList();
                int totalPages = (int)Math.Ceiling((double)participants.Count / pageSize);

                if (page > totalPages)
                {
                    player.ChatMessage($"Invalid page number. There are only {totalPages} page(s).");
                    return;
                }

                var participantList = string.Join("\n", participants.Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(p => $"{p.Name}: {p.Score} Points"));

                Notify("ParticipantsAndScores", null, placeholders: new Dictionary<string, string>
                {
                    {"Page", page.ToString()},
                    {"TotalPages", totalPages.ToString()},
                    {"PlayerList", participantList}
                });

                player.ChatMessage("Use /status_tournament <page_number> to view more.");
            }
            else if (tournamentStartTime > DateTime.UtcNow)
            {
                string timeRemainingToStart = FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow);
                player.ChatMessage($"Tournament not running. Next start in {timeRemainingToStart}.");
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
