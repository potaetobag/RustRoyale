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
    [Info("RustRoyale", "Potaetobag", "1.0.4"), Description("Rust Royale custom tournament game mode with point-based scoring system.")]
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
            public string Timezone { get; set; } = "UTC"; // Default to UTC timezone
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
                {"StartTournament", "Brace yourselves, champions! The tournament has begun! Time to show off those pro skills (or hilarious fails). Time left: {TimeRemaining}. Duration: {Duration} hours."},
                {"EndTournament", "The tournament is over! Congrats to the winners, and for the rest... better luck next time (maybe practice a bit?)."},
                {"PlayerScoreUpdate", "{PlayerName} just bagged {Score} point{PluralS} for {Action}. Somebody's on fire! 🔥"},
                {"TopPlayers", "Leaderboard time! Top {Count} players are: {PlayerList}. Did your name make the cut, or are you just here for fun? 😅"},
                {"TimeRemaining", "Tick-tock! Time remaining in the tournament: {Time}. Don't waste it—score some points!"},
                {"JoinTournament", "{PlayerName} has entered the fray! Grab the popcorn, this should be good. 🍿"},
                {"LeaveTournament", "{PlayerName} has exited the battlefield. Maybe they got scared? We’ll never know. 🤷‍♂️"},
                {"KillNPC", "{PlayerName} earned {Score} point{PluralS} for bravely taking down an NPC! Total score: {TotalScore}. The NPC didn’t stand a chance. 💪"},
                {"KillEntity", "{PlayerName} earned {Score} point{PluralS} for obliterating a {EntityType}! Total score: {TotalScore}. BOOM! 💥"},
                {"KillPlayer", "{PlayerName} earned {Score} point{PluralS} for sending {VictimName} to respawn land! Total score: {TotalScore}. Savage! 😈"},
                {"SelfInflictedDeath", "Oops! {PlayerName} lost {Score} point{PluralS} for a self-inflicted oopsie. Total score: {TotalScore}. Smooth move, buddy. 🤦"},
                {"DeathByNPC", "Yikes! {PlayerName} lost {Score} point{PluralS} for getting clobbered by an NPC. Total score: {TotalScore}. They’re probably laughing at you. 😭"},
                {"NoTournamentRunning", "Hold your horses! There's no tournament right now. Next round starts in {TimeRemainingToStart}. Grab a snack meanwhile! 🍪"},
                {"ParticipantsAndScores", "Scoreboard time! (Page {Page}/{TotalPages}): {PlayerList}. Who’s crushing it? Who’s just chilling?"},
                {"NotInTournament", "Uh-oh! You’re not part of the tournament. Join in, don’t be shy!"},
                {"NoPermission", "Sorry, you don’t have permission to {ActionName}. Maybe ask the admins for a favor?"},
                {"AlreadyParticipating", "Relax, {PlayerName}. You’re already in the tournament. No need to double-dip!"},
                {"AlreadyOptedIn", "Nice try, {PlayerName}, but you’re already opted in. Eager much? 😜"},
                {"OptedOutTournament", "{PlayerName} has decided to opt out. Bye-bye! Don’t let FOMO get you. 👋"},
                {"NotOptedInTournament", "You weren’t even opted in, {PlayerName}. Why so dramatic? 🙄"},
                {"TournamentNotRunning", "Patience is a virtue, {PlayerName}. No tournament now. Next round starts in {TimeRemainingToStart}. Go sharpen your skills! ⚔️"},
                {"TournamentScores", "Here’s the rundown of scores: {PlayerList}. Is your name shining, or are you just here for the jokes? 😂"},
                {"TournamentAlreadyRunning", "Whoa there! A tournament is already underway. Time left: {TimeRemaining}. Jump in or cheer from the sidelines! 🎉"},
                {"NoScores", "No scores available yet. Join the tournament and make some history!"},
                {"TournamentAboutToStart", "The tournament is about to start! Opt-in now to participate."},
                {"TournamentCountdown", "Tournament starting soon! {Time} left to join."}
            };
        }
    
    #endregion

    #region Configuration Handling
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

            // Validate StartDay
            if (!Enum.TryParse(Configuration.StartDay, true, out DayOfWeek _))
            {
                PrintWarning("Invalid StartDay in configuration. Defaulting to Friday.");
                Configuration.StartDay = "Friday";
                updated = true;
            }

            // Validate StartHour
            if (Configuration.StartHour < 0 || Configuration.StartHour > 23)
            {
                PrintWarning("Invalid StartHour in configuration. Defaulting to 14 (2 PM).");
                Configuration.StartHour = 14;
                updated = true;
            }

            // Validate DurationHours (New Addition)
            if (Configuration.DurationHours <= 0 || Configuration.DurationHours > 168) // Limit to 1 week
            {
                PrintWarning("Invalid DurationHours in configuration. Defaulting to 72 hours.");
                Configuration.DurationHours = 72;
                updated = true;
            }

            // Validate DataRetentionDays
            if (Configuration.DataRetentionDays <= 0)
            {
                PrintWarning("Invalid DataRetentionDays in configuration. Defaulting to 30 days.");
                Configuration.DataRetentionDays = 30;
                updated = true;
            }

            // Validate TopPlayersToTrack
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

            // Save updated configuration
            if (updated)
            {
                SaveConfig();
                Puts("Configuration updated with validated defaults.");
            }

            // Log validated configuration values
            Puts($"Configuration validated: StartDay={Configuration.StartDay}, StartHour={Configuration.StartHour}, DurationHours={Configuration.DurationHours}, DataRetentionDays={Configuration.DataRetentionDays}, TopPlayersToTrack={Configuration.TopPlayersToTrack}, NotificationIntervals={string.Join(", ", Configuration.NotificationIntervals)}.");
        }
    
    #endregion

    #region Plugin Handle
        private void OnUnload()
        {
            CleanupTimers();

            if (saveDataTimer != null)
            {
                saveDataTimer.Destroy();
                saveDataTimer = null;
            }

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
    
    #endregion

    #region Timezone Handling
        private TimeZoneInfo GetTimezone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(Configuration.Timezone);
            }
            catch (TimeZoneNotFoundException)
            {
                PrintWarning($"Invalid Timezone '{Configuration.Timezone}'. Defaulting to UTC.");
                return TimeZoneInfo.Utc;
            }
            catch (Exception ex)
            {
                PrintError($"Error loading timezone '{Configuration.Timezone}': {ex.Message}");
                return TimeZoneInfo.Utc;
            }
        }

        private DateTime GetLocalTime(DateTime utcTime)
        {
            var timezone = GetTimezone();
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, timezone);
        }

        private DateTime GetUtcTime(DateTime localTime)
        {
            var timezone = GetTimezone();
            return TimeZoneInfo.ConvertTimeToUtc(localTime, timezone);
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

            Puts($"Initialization complete. Tournament duration set to {Configuration.DurationHours} hours.");
        }

        private bool ValidateAdminCommand(BasePlayer player, string actionName)
        {
            if (!HasAdminPermission(player))
            {
                Notify("NoPermission", player, placeholders: new Dictionary<string, string>
                {
                    { "ActionName", actionName }
                });
                return false;
            }
            return true;
        }
    
    #endregion

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

    #region Schedule Tournament
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

        private string FormatTimeRemaining(TimeSpan timeSpan)
        {
            timeSpan = timeSpan < TimeSpan.Zero ? TimeSpan.Zero : timeSpan;

            var parts = new List<string>();

            if (timeSpan.Days > 0)
                parts.Add($"{timeSpan.Days} day{(timeSpan.Days > 1 ? "s" : "")}");
            if (timeSpan.Hours > 0)
                parts.Add($"{timeSpan.Hours} hour{(timeSpan.Hours > 1 ? "s" : "")}");
            if (timeSpan.Minutes > 0)
                parts.Add($"{timeSpan.Minutes} minute{(timeSpan.Minutes > 1 ? "s" : "")}");
            if (timeSpan.Seconds > 0)
                parts.Add($"{timeSpan.Seconds} second{(timeSpan.Seconds > 1 ? "s" : "")}");

            return string.Join(", ", parts);
        }

        private void ScheduleCountdown(DateTime targetTime, Action onCompletion)
        {
            TimeSpan timeUntilTarget = targetTime - DateTime.UtcNow;

            if (timeUntilTarget.TotalSeconds <= 0)
            {
                Puts($"[Debug] Timer expired immediately for target: {targetTime}. Invoking completion.");
                if (onCompletion != null)
                {
                    Puts("[Debug] Invoking onCompletion callback.");
                    try
                    {
                        onCompletion.Invoke();
                    }
                    catch (Exception ex)
                    {
                        PrintError($"[Error] Exception during immediate onCompletion execution: {ex.Message}");
                    }
                }
                else
                {
                    Puts("[Debug] onCompletion is null. Skipping invocation.");
                }
                return;
            }

            Puts($"[Debug] Starting countdown timer for {timeUntilTarget.TotalSeconds} seconds.");
            countdownTimer?.Destroy();

            countdownTimer = timer.Repeat(1f, (int)timeUntilTarget.TotalSeconds, () =>
            {
                TimeSpan remainingTime = targetTime - DateTime.UtcNow;

                if (remainingTime.TotalSeconds <= 0)
                {
                    Puts("[Debug] Countdown timer expired; invoking completion.");
                    countdownTimer?.Destroy();
                    countdownTimer = null;

                    if (onCompletion != null)
                    {
                        Puts("[Debug] Invoking onCompletion callback.");
                        try
                        {
                            onCompletion.Invoke();
                        }
                        catch (Exception ex)
                        {
                            PrintError($"[Error] Exception during onCompletion execution: {ex.Message}");
                        }
                    }
                    else
                    {
                        Puts("[Debug] onCompletion is null. Skipping invocation.");
                    }
                    return;
                }

                Puts($"[Debug] Timer tick. Remaining time: {remainingTime.TotalSeconds}s.");
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
                DayOfWeek startDay = Enum.Parse<DayOfWeek>(Configuration.StartDay, true);
                DateTime now = DateTime.UtcNow;
                DateTime localNow = GetLocalTime(now);

                DateTime nextStart = localNow.AddDays((7 + (int)startDay - (int)localNow.DayOfWeek) % 7).Date.AddHours(Configuration.StartHour);

                if (nextStart <= localNow)
                {
                    nextStart = nextStart.AddDays(7);
                }

                tournamentStartTime = GetUtcTime(nextStart);
                LogEvent($"Tournament scheduled to start on {GetLocalTime(tournamentStartTime):yyyy-MM-dd HH:mm:ss} ({Configuration.Timezone}).");

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
            DateTime nextReminder = now.Date.AddHours(Configuration.StartHour);

            if (nextReminder <= now)
            {
                nextReminder = nextReminder.AddDays(1);
            }

            LogEvent($"Daily reminder scheduled for {nextReminder:yyyy-MM-dd HH:mm:ss} UTC.");

            ScheduleCountdown(nextReminder, () =>
            {
                NotifyTournamentStartImminent();
                ScheduleDailyCountdown();
            });
        }

        private int lastNotifiedMinutes = -1;

        private void NotifyTournamentCountdown(TimeSpan remainingTime)
        {
            int remainingSeconds = (int)remainingTime.TotalSeconds;

            Puts($"[Debug] Countdown check: {remainingSeconds}s remaining. Last notified: {lastNotifiedMinutes}s");

            if (Configuration.NotificationIntervals.Contains(remainingSeconds) && lastNotifiedMinutes != remainingSeconds)
            {
                lastNotifiedMinutes = remainingSeconds;

                string formattedTime = FormatTimeRemaining(remainingTime);
                Puts($"[Debug] Sending tournament countdown message: {formattedTime}");

                Notify("TournamentCountdown", null, placeholders: new Dictionary<string, string>
                {
                    { "Time", formattedTime }
                });

                // Discord notification
                if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
                {
                    string discordMessage = FormatMessage(Configuration.MessageTemplates["TournamentCountdown"], new Dictionary<string, string>
                    {
                        { "Time", formattedTime }
                    });

                    Puts($"[Debug] Sending Discord countdown message: {discordMessage}");
                    SendDiscordMessage(discordMessage);
                }
            }
        }

        private void NotifyTournamentStartImminent()
        {
            Notify("TournamentAboutToStart", null, placeholders: new Dictionary<string, string>
            {
                { "Time", FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow) }
            });

            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                SendDiscordMessage($"The tournament is about to start in {FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow)}. Don't miss it!");
            }
        }

    #endregion

    #region Tournament Logic
        [ChatCommand("start_tournament")]
        private void StartTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (!ValidateAdminCommand(player, "start the tournament"))
            {
                return;
            }

            // Check if the tournament is already running
            if (isTournamentRunning)
            {
            Puts("[Debug] Tournament already running.");
            return;
            } 
            {
                string message = FormatMessage(Configuration.MessageTemplates["TournamentAlreadyRunning"], new Dictionary<string, string>
                {
                    { "TimeRemaining", FormatTimeRemaining(tournamentEndTime - DateTime.UtcNow) }
                });
                SendPlayerMessage(player, message);
                return;
            }

            StartTournament();

            // Notify all players in the global chat
            string globalMessage = FormatMessage(Configuration.MessageTemplates["StartTournament"], new Dictionary<string, string>
            {
                { "TimeRemaining", FormatTimeRemaining(tournamentEndTime - DateTime.UtcNow) },
                { "Duration", Configuration.DurationHours.ToString() }
            });

            SendTournamentMessage(globalMessage); // Global chat notification

            // Notify Discord
            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                SendDiscordMessage(globalMessage); // Discord notification
            }

            Puts($"[Debug] Notifications sent for tournament start: {globalMessage}");
        }

        private void StartTournament()
        {
            Puts("[Debug] StartTournament invoked.");

            try
            {
                // Check if a tournament is already running
                if (isTournamentRunning)
                {
                    Puts("[Debug] Tournament already running.");
                    return; // Remove the duplicate Notify call here
                }

                // Validate tournament duration
                if (Configuration.DurationHours <= 0)
                {
                    PrintError("Invalid tournament duration. Ensure `DurationHours` in the configuration is greater than 0.");
                    return;
                }

                // Log tournament start
                Puts($"RustRoyale: Tournament started with duration {Configuration.DurationHours} hours!");

                // Set tournament state and timing
                Puts("[Debug] StartTournament invoked.");
                isTournamentRunning = true;
                tournamentEndTime = DateTime.UtcNow.AddHours(Configuration.DurationHours);

                // Log the calculated end time
                Puts($"Tournament end time set to: {tournamentEndTime:yyyy-MM-dd HH:mm:ss} UTC.");

                // Reset player stats for the tournament
                playerStats.Clear();
                foreach (var participant in participantsData.Values)
                {
                    participant.Score = 0;
                }

                // Clear existing countdown timer if active
                countdownTimer?.Destroy();
                countdownTimer = null;
                Puts("[Debug] Countdown timer destroyed.");

                // Log participant list for debugging and record-keeping
                if (participantsData.Values.Any())
                {
                    LogEvent($"Participants at tournament start: {string.Join(", ", participantsData.Values.Select(p => p.Name))}");
                }
                else
                {
                    LogEvent("No participants at tournament start.");
                }

                // Log event in the tournament file
                LogEvent("Tournament started successfully.");
            }
            catch (Exception ex)
            {
                // Log any errors that occur during tournament start
                PrintError($"Failed to start tournament: {ex.Message}");
            }
        }

        [ChatCommand("end_tournament")]
        private void EndTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (!ValidateAdminCommand(player, "end the tournament"))
            {
                return;
            }

            if (!isTournamentRunning)
            {
                string message = FormatMessage(Configuration.MessageTemplates["NoTournamentRunning"], new Dictionary<string, string>
                {
                    { "TimeRemainingToStart", tournamentStartTime > DateTime.UtcNow ? FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow) : "N/A" }
                });
                SendPlayerMessage(player, message);
                return;
            }

            // End the tournament
            EndTournament();

            // Prepare leaderboard/results
            var sortedParticipants = participantsData.Values
                .OrderByDescending(p => p.Score)
                .Take(Configuration.TopPlayersToTrack) // Get the top N players
                .ToList();

            string resultsMessage = "Leaderboard:\n";
            if (sortedParticipants.Any())
            {
                resultsMessage += string.Join("\n", sortedParticipants.Select((p, index) => $"{index + 1}. {p.Name} - {p.Score} Points"));
            }
            else
            {
                resultsMessage += "No participants scored points in this tournament.";
            }

            // Global chat notification
            string globalMessage = FormatMessage(Configuration.MessageTemplates["EndTournament"], new Dictionary<string, string>());
            globalMessage += $"\n{resultsMessage}";
            SendTournamentMessage(globalMessage);

            // Discord notification
            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                SendDiscordMessage(globalMessage);
            }

            // Log the results to the console for debug purposes
            Puts($"[RustRoyale] [Debug] Notifications sent for tournament end: {globalMessage}");
        }

        private void EndTournament()
        {
            Puts("[Debug] EndTournament invoked.");

            try
            {
                if (!isTournamentRunning)
                {
                    Puts("[Debug] No tournament is currently running.");
                    return; // Notifications are handled in the command method
                }

                // Mark the tournament as no longer running
                isTournamentRunning = false;

                // Log tournament end
                Puts("RustRoyale: Tournament ended!");

                // Sort participants by score
                var sortedParticipants = participantsData.Values
                    .OrderByDescending(p => p.Score)
                    .ToList();

                // Save tournament history
                SaveTournamentHistory(sortedParticipants);

                // Log the end of the tournament in the event file
                LogEvent("Tournament ended successfully.");

                // Cleanup timers if active
                countdownTimer?.Destroy();
                countdownTimer = null;

                // Reset stats for participants (optional, depending on desired behavior)
                foreach (var participant in participantsData.Values)
                {
                    participant.Score = 0;
                }

                Puts($"[Debug] Tournament successfully ended. Total participants: {sortedParticipants.Count}");
            }
            catch (Exception ex)
            {
                // Log any errors during the end of the tournament
                PrintError($"Failed to end tournament: {ex.Message}");
            }
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
            if (!isTournamentRunning || victim == null)
            {
                Puts("[Debug] Tournament is not running or victim is null.");
                return;
            }

            var attacker = info?.InitiatorPlayer;
            bool isVictimNPC = victim.ShortPrefabName.Contains("murderer") ||
                            victim.ShortPrefabName.Contains("zombie") ||
                            victim.ShortPrefabName.Contains("scientist") ||
                            victim.ShortPrefabName.Contains("scarecrow");

            bool isVictimEntity = victim.ShortPrefabName.Contains("helicopter") ||
                                victim.ShortPrefabName.Contains("bradley");

            Puts($"[Debug] Victim: {victim.displayName} ({victim.ShortPrefabName}), IsNPC: {isVictimNPC}, IsEntity: {isVictimEntity}");

            // Player kills an NPC
            if (isVictimNPC && attacker != null && !attacker.IsNpc && participants.Contains(attacker.userID))
            {
                if (!Configuration.ScoreRules.TryGetValue("NPC", out int points))
                {
                    Puts("[Debug] ScoreRules does not contain the key 'NPC' or the value is invalid.");
                    return;
                }

                // Update the player's score and notify
                UpdatePlayerScore(attacker.userID, "NPC", "killing an NPC");

                // Logging for debugging
                Puts($"[Debug] Score updated and notification sent for {attacker.displayName} killing an NPC.");
                return;
            }

            // Player destroys a Helicopter or Bradley
            if (isVictimEntity && attacker != null && !attacker.IsNpc && participants.Contains(attacker.userID))
            {
                if (!Configuration.ScoreRules.TryGetValue("ENT", out int points))
                {
                    Puts("[Debug] ScoreRules does not contain the key 'ENT' or the value is invalid.");
                    return;
                }

                // Update the player's score and notify
                UpdatePlayerScore(attacker.userID, "ENT", "destroying a Helicopter or Bradley");

                // Logging for debugging
                Puts($"[Debug] Score updated and notification sent for {attacker.displayName} destroying an entity.");
                return;
            }

            // Player is killed by an NPC
            if ((attacker == null || attacker.IsNpc) && participants.Contains(victim.userID))
            {
                if (!Configuration.ScoreRules.TryGetValue("JOKE", out int points))
                {
                    Puts("[Debug] ScoreRules does not contain the key 'JOKE' or the value is invalid.");
                    return;
                }

                // Update the player's score for being killed by an NPC
                UpdatePlayerScore(victim.userID, "JOKE", "being killed by an NPC");

                Puts($"[Debug] Notification sent for {victim.displayName} being killed by an NPC.");
                return;
            }

            // Player kills themselves (self-inflicted)
            if (attacker == victim && participants.Contains(victim.userID))
            {
                if (!Configuration.ScoreRules.TryGetValue("JOKE", out int points))
                {
                    Puts("[Debug] ScoreRules does not contain the key 'JOKE' or the value is invalid.");
                    return;
                }

                UpdatePlayerScore(victim.userID, "JOKE", "self-inflicted death");
                Puts($"[Debug] Notification sent for {victim.displayName} self-inflicted death.");
                return;
            }

            // Player kills another player
            if (attacker != null && participants.Contains(attacker.userID) && participants.Contains(victim.userID))
            {
                if (!Configuration.ScoreRules.TryGetValue("DEAD", out int pointsForVictim) ||
                    !Configuration.ScoreRules.TryGetValue("KILL", out int pointsForAttacker))
                {
                    Puts("[Debug] ScoreRules does not contain keys 'DEAD' or 'KILL' or their values are invalid.");
                    return;
                }

                UpdatePlayerScore(victim.userID, "DEAD", $"being killed by {attacker.displayName}");
                Puts($"[Debug] Notifications sent for dead interaction between {attacker.displayName} and {victim.displayName}.");

                UpdatePlayerScore(attacker.userID, "KILL", $"killing {victim.displayName}");
                Puts($"[Debug] Notifications sent for kill interaction between {attacker.displayName} and {victim.displayName}.");
            }
        }
    #endregion

    #region Score Handling

        private string GetPlayerName(ulong userId)
        {
            // Check the cache first
            if (playerNameCache.TryGetValue(userId, out var cachedName))
            {
                return cachedName;
            }

            // Check if the player is currently online
            var player = BasePlayer.FindByID(userId) ?? BasePlayer.FindSleeping(userId);
            if (player != null && !string.IsNullOrEmpty(player.displayName))
            {
                playerNameCache[userId] = player.displayName;
                return player.displayName;
            }

            // Check participantsData for the player's name
            if (participantsData.TryGetValue(userId, out var participant) && !string.IsNullOrEmpty(participant.Name))
            {
                playerNameCache[userId] = participant.Name;
                return participant.Name;
            }

            // Fallback: Load from the Participants.json file
            try
            {
                if (File.Exists(ParticipantsFile))
                {
                    var participantsFromFile = JsonConvert.DeserializeObject<Dictionary<ulong, PlayerStats>>(File.ReadAllText(ParticipantsFile));
                    if (participantsFromFile != null && participantsFromFile.TryGetValue(userId, out var participantFromFile))
                    {
                        if (!string.IsNullOrEmpty(participantFromFile.Name))
                        {
                            playerNameCache[userId] = participantFromFile.Name;
                            return participantFromFile.Name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to retrieve player name from Participants.json: {ex.Message}");
            }

            // Final fallback to "Unknown"
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
                    // Load participants data from file
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

                    // Synchronize participants set with participantsData
                    participants.Clear();
                    foreach (var userId in participantsData.Keys)
                    {
                        participants.Add(userId);
                    }

                    Puts($"[Debug] Loaded participants: {string.Join(", ", participants)}");
                }
                else
                {
                    // If the file doesn't exist, initialize with empty data
                    participantsData = new ConcurrentDictionary<ulong, PlayerStats>();
                    participants.Clear();
                    PrintWarning("Participants data file is missing or corrupt. Starting with an empty dataset.");
                    SaveParticipantsData(); // Create the initial file if it doesn't exist
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions and initialize with empty data
                PrintWarning($"Failed to load participants data: {ex.Message}. Initializing with an empty dictionary.");
                participantsData = new ConcurrentDictionary<ulong, PlayerStats>();
                participants.Clear();
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

            // Calculate pluralization for points
            string pluralS = Math.Abs(points) == 1 ? "" : "s";

            // Prepare the message for global chat and Discord
            string globalMessage = FormatMessage(Configuration.MessageTemplates["PlayerScoreUpdate"], new Dictionary<string, string>
            {
                { "PlayerName", BasePlayer.FindByID(userId)?.displayName ?? "Unknown" },
                { "Score", points.ToString() },
                { "TotalScore", participant.Score.ToString() },
                { "Action", actionDescription },
                { "PluralS", Math.Abs(points) == 1 ? "" : "s" }
            });

            // Notify all players in the global chat
            SendTournamentMessage(globalMessage); // Global chat notification

            // Notify Discord
            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                SendDiscordMessage(globalMessage); // Discord notification
            }

            // Log the score update
            LogEvent($"{participant.Name} received {points} point{pluralS} for {actionDescription}. Total score: {participant.Score}.");
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

        private ConcurrentDictionary<ulong, string> playerNameCache = new ConcurrentDictionary<ulong, string>();

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

        private void AnnounceWinners(List<PlayerStats> sortedParticipants)
        {
            var topPlayers = sortedParticipants.Take(Configuration.TopPlayersToTrack).ToList();

            if (!topPlayers.Any())
            {
                Notify("NoScores", null);
                Puts("[Debug] No top players to announce.");
                return;
            }

            // Format the top players into a readable list
            var playerList = string.Join("\n", topPlayers.Select((p, i) => $"{i + 1}. {p.Name} ({p.Score} Points)"));

            // Notify all players in global chat and Discord
            var message = FormatMessage(Configuration.MessageTemplates["TournamentWinners"], new Dictionary<string, string>
            {
                { "PlayerCount", topPlayers.Count.ToString() },
                { "PlayerList", playerList }
            });

            SendTournamentMessage(message); // Notify in global chat
            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                SendDiscordMessage(message); // Notify in Discord
            }

            LogEvent($"Announced winners: {string.Join(", ", topPlayers.Select(p => p.Name))}");
        }

    #endregion

    #region Notifications Handling

        private string Notify(string templateName, BasePlayer player, int score = 0, string action = "", string entityType = "", string victimName = "", Dictionary<string, string> placeholders = null)
        {
            if (!Configuration.MessageTemplates.TryGetValue(templateName, out var template))
            {
                PrintWarning($"Template '{templateName}' not found in configuration.");
                return null;
            }

            // Prepare default placeholders
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

            // Replace placeholders in the template
            foreach (var placeholder in placeholders)
            {
                template = template.Replace($"{{{placeholder.Key}}}", placeholder.Value);
            }

            return template;
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
                {"PlayerScoreUpdate", "{PlayerName} earned {Score} point{PluralS} for {Action}. Total score: {TotalScore}."},
                {"TopPlayers", "Top {Count} players: {PlayerList}."},
                {"TimeRemaining", "Time remaining in the tournament: {Time}."},
                {"TournamentWinners", "The tournament is over! Here are the winners:\n{PlayerList}"}
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

        private void SendPlayerMessage(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message))
            {
                PrintWarning("[SendPlayerMessage] Player is null or message is empty.");
                return;
            }

            player.SendConsoleCommand("chat.add", Configuration.ChatIconSteamId, Configuration.ChatUsername, message);
        }

        private void SendTournamentMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                PrintWarning("[SendTournamentMessage] Attempted to send an empty or null message.");
                return;
            }

            Puts($"[Debug] Sending message: {message}, SteamID: {Configuration.ChatIconSteamId}, Username: {Configuration.ChatUsername}");

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                {
                    PrintWarning($"[SendTournamentMessage] Skipping player: {player?.displayName ?? "Unknown"} (Disconnected or null)");
                    continue;
                }

                player.SendConsoleCommand("chat.add", Configuration.ChatIconSteamId, Configuration.ChatUsername, message);
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

        private void LogMessage(string message, bool logToDiscord = false)
        {
            Puts(message);

            if (logToDiscord && !string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                SendDiscordMessage(message);
            }
        }

    #endregion

    #region Commands
        
        [ChatCommand("time_tournament")]
        private void TimeTournamentCommand(BasePlayer player, string command, string[] args)
        {
            string message = GetTimeRemainingMessage();
            SendPlayerMessage(player, message); // Send only to the command initiator.
        }

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

        [ChatCommand("enter_tournament")]
        private void EnterTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (!isTournamentRunning)
            {
                // Calculate the time remaining to the next tournament start
                string timeRemainingToStart = tournamentStartTime > DateTime.UtcNow
                    ? FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow)
                    : "unknown"; // Fallback if tournamentStartTime is not set correctly

                // Notify the player that the tournament is not running
                string message = Configuration.MessageTemplates.TryGetValue("TournamentNotRunning", out var template)
                    ? FormatMessage(template, new Dictionary<string, string>
                    {
                        { "TimeRemainingToStart", timeRemainingToStart },
                        { "PlayerName", player.displayName }
                    })
                    : "The tournament is not currently running.";

                SendPlayerMessage(player, message);
                return;
            }

            if (participantsData.TryAdd(player.userID, new PlayerStats(player.userID)))
            {
                // Save participant data
                SaveParticipantsData();

                // Notify the player using the configured template
                string message = Configuration.MessageTemplates.TryGetValue("JoinTournament", out var template)
                    ? FormatMessage(template, new Dictionary<string, string>
                    {
                        { "PlayerName", player.displayName }
                    })
                    : $"{player.displayName} has joined the tournament.";

                SendPlayerMessage(player, message);

                // Log the event
                LogEvent($"{player.displayName} joined the current tournament.");
            }
            else
            {
                // Notify the player they are already participating
                string message = Configuration.MessageTemplates.TryGetValue("AlreadyParticipating", out var template)
                    ? FormatMessage(template, new Dictionary<string, string>
                    {
                        { "PlayerName", player.displayName }
                    })
                    : $"{player.displayName}, you are already in the tournament.";

                SendPlayerMessage(player, message);
            }
        }

        [ChatCommand("exit_tournament")]
        private void ExitTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (participantsData.TryRemove(player.userID, out _))
            {
                // Save participant data
                SaveParticipantsData();

                // Notify the player using the configured template
                string message = Configuration.MessageTemplates.TryGetValue("LeaveTournament", out var template)
                    ? FormatMessage(template, new Dictionary<string, string>
                    {
                        { "PlayerName", player.displayName }
                    })
                    : $"{player.displayName} has left the tournament.";

                SendPlayerMessage(player, message);

                // Log the event
                LogEvent($"{player.displayName} left the tournament.");
            }
            else
            {
                // Notify the player they are not part of the tournament
                string message = Configuration.MessageTemplates.TryGetValue("NotInTournament", out var template)
                    ? FormatMessage(template, new Dictionary<string, string>
                    {
                        { "PlayerName", player.displayName }
                    })
                    : $"{player.displayName}, you are not part of the tournament.";

                SendPlayerMessage(player, message);
            }
        }

        [ChatCommand("open_tournament")]
        private void OpenTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (participantsData.ContainsKey(player.userID))
            {
                Puts($"[Debug] {player.displayName} ({player.userID}) attempted to opt-in but is already in participantsData.");
                string message = Configuration.MessageTemplates.TryGetValue("AlreadyOptedIn", out var alreadyOptedInTemplate)
                    ? FormatMessage(alreadyOptedInTemplate, new Dictionary<string, string>
                    {
                        { "PlayerName", player.displayName }
                    })
                    : $"{player.displayName}, you are already opted into the tournament.";
                SendPlayerMessage(player, message);
                return;
            }

            // Add the player to the participants data
            participantsData[player.userID] = new PlayerStats(player.userID);

            // Add the player to openTournamentPlayers
            if (!openTournamentPlayers.Contains(player.userID))
            {
                openTournamentPlayers.Add(player.userID);
                Puts($"[Debug] {player.displayName} ({player.userID}) added to openTournamentPlayers.");
            }

            // Save the updated participants data
            SaveParticipantsData();

            // Notify the player that they have opted in successfully
            string successMessage = Configuration.MessageTemplates.TryGetValue("JoinTournament", out var joinTournamentTemplate)
                ? FormatMessage(joinTournamentTemplate, new Dictionary<string, string>
                {
                    { "PlayerName", player.displayName }
                })
                : $"{player.displayName} has successfully opted into the tournament.";
            SendPlayerMessage(player, successMessage);

            LogEvent($"{player.displayName} opted into the tournament.");
        }

        [ChatCommand("close_tournament")]
        private void CloseTournamentCommand(BasePlayer player, string command, string[] args)
        {
            // Check if the player is in participantsData
            if (participantsData.TryRemove(player.userID, out _))
            {
                // Save updated participants data
                SaveParticipantsData();

                // Log successful removal
                Puts($"[Debug] {player.displayName} ({player.userID}) removed from participantsData.");

                // Notify the player they have opted out
                string message = Configuration.MessageTemplates.TryGetValue("OptedOutTournament", out var optedOutTemplate)
                    ? FormatMessage(optedOutTemplate, new Dictionary<string, string>
                    {
                        { "PlayerName", player.displayName }
                    })
                    : $"{player.displayName} has opted out of the tournament.";
                SendPlayerMessage(player, message);

                LogEvent($"{player.displayName} opted out of the tournament.");
            }
            else
            {
                // Log state for debugging
                Puts($"[Debug] {player.displayName} ({player.userID}) was not found in participantsData.");
                Puts($"[Debug] Current participantsData: {string.Join(", ", participantsData.Keys.Select(id => $"{id} ({GetPlayerName(id)})"))}");

                // Notify the player they were not part of the tournament
                string message = Configuration.MessageTemplates.TryGetValue("NotInTournament", out var notInTournamentTemplate)
                    ? FormatMessage(notInTournamentTemplate, new Dictionary<string, string>
                    {
                        { "PlayerName", player.displayName }
                    })
                    : $"{player.displayName}, you are not part of the tournament.";
                SendPlayerMessage(player, message);
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
                // Display the time remaining in the tournament
                string timeRemainingMessage = GetTimeRemainingMessage();
                SendPlayerMessage(player, timeRemainingMessage);

                // Get the list of participants and their scores
                var participants = participantsData.Values.OrderByDescending(p => p.Score).ToList();

                if (participants.Count == 0)
                {
                    SendPlayerMessage(player, "There are no participants in the tournament yet.");
                    return;
                }

                int totalPages = (int)Math.Ceiling((double)participants.Count / pageSize);

                if (page > totalPages)
                {
                    SendPlayerMessage(player, $"Invalid page number. There are only {totalPages} page(s).");
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

                SendPlayerMessage(player, "Use /status_tournament <page_number> to view more.");
            }
            else
            {
                // Calculate time remaining to the next tournament start if available
                string timeRemainingToStart = tournamentStartTime > DateTime.UtcNow
                    ? FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow)
                    : "N/A";

                // Notify using the "TournamentNotRunning" template
                Notify("TournamentNotRunning", player, placeholders: new Dictionary<string, string>
                {
                    { "TimeRemainingToStart", timeRemainingToStart },
                    { "PlayerName", player.displayName } // Added the missing placeholder
                });

                // Inform the player that no scores are available if the tournament hasn't started
                SendPlayerMessage(player, "The tournament has not started yet, so there's nothing to show.");
            }
        }

        [ChatCommand("score_tournament")]
        private void ScoreTournamentCommand(BasePlayer player, string command, string[] args)
        {
            DisplayScores();
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

            Notify("TournamentScores", null, placeholders: new Dictionary<string, string>
            {
                {"PlayerList", scoreList}
            });
        }

        private bool HasAdminPermission(BasePlayer player)
        {
            return permission.UserHasPermission(player.UserIDString, AdminPermission);
        }

        [ChatCommand("reload_config")]
        private void ReloadConfigCommand(BasePlayer player, string command, string[] args)
        {
            if (!ValidateAdminCommand(player, "reload the configuration"))
            {
                return;
            }

            try
            {
                // Reload configuration from file
                LoadConfig();
                ValidateConfiguration();

                // Apply the new configuration
                ApplyConfiguration();

                // Notify the admin about the success
                SendPlayerMessage(player, "Configuration has been reloaded successfully and applied.");

                // Log who reloaded the configuration
                Puts($"[Debug] Configuration reloaded successfully by {player.displayName} ({player.UserIDString}).");

                // Reapply scheduling if AutoStartEnabled is true
                if (Configuration.AutoStartEnabled)
                {
                    ScheduleTournament();
                }
                else
                {
                    Puts("AutoStart is disabled. No tournament has been scheduled.");
                }
            }
            catch (Exception ex)
            {
                // Handle errors and inform the admin
                PrintError($"Failed to reload configuration: {ex.Message}");
                SendPlayerMessage(player, "Failed to reload configuration. Check the server logs for details.");
            }
        }

        private void ApplyConfiguration()
        {
            // Reflect timezone changes
            UpdateTimeZoneDependentLogic();

            // Reset notification logic
            lastNotifiedMinutes = -1;

            // Log the updated configuration dynamically
            LogConfiguration(Configuration);

            // Inform if a tournament is running
            if (isTournamentRunning)
            {
                Puts("A tournament is currently running. Changes to scoring or duration will apply to the next tournament.");
            }

            // Apply scoring rules or other dynamic configurations
            ApplyScoringRules();
        }

        private void LogConfiguration(ConfigData config)
        {
            Puts("Current Configuration:");
            foreach (var property in typeof(ConfigData).GetProperties())
            {
                try
                {
                    var value = property.GetValue(config);
                    string displayValue = value is IEnumerable<object> collection
                        ? string.Join(", ", collection)
                        : value?.ToString() ?? "null";

                    Puts($"  {property.Name}: {displayValue}");
                }
                catch (Exception ex)
                {
                    PrintWarning($"Error logging configuration property '{property.Name}': {ex.Message}");
                }
            }
        }

        private void UpdateTimeZoneDependentLogic()
        {
            // Logic to update based on the new timezone
            try
            {
                var timezone = GetTimezone();
                Puts($"Timezone updated to {timezone.DisplayName}.");
            }
            catch (Exception ex)
            {
                PrintError($"Failed to update timezone logic: {ex.Message}");
            }
        }

        private void ApplyScoringRules()
        {
            // Example: Reload scoring rules dynamically (if needed)
            if (Configuration.ScoreRules != null && Configuration.ScoreRules.Count > 0)
            {
                Puts("Scoring rules applied:");
                foreach (var rule in Configuration.ScoreRules)
                {
                    Puts($"  {rule.Key}: {rule.Value} points");
                }
            }
        }

        [ChatCommand("show_rules")]
        private void ShowRulesCommand(BasePlayer player, string command, string[] args)
        {
            if (!ValidateAdminCommand(player, "view tournament rules"))
                return;

            var rules = Configuration.ScoreRules
                .Select(r => $"{r.Key}: {r.Value} points")
                .Aggregate((a, b) => $"{a}\n{b}");

            SendPlayerMessage(player, $"Tournament Rules:\n{rules}");
        }

        [ChatCommand("help_tournament")]
        private void HelpTournamentCommand(BasePlayer player, string command, string[] args)
        {
            // Define command descriptions dynamically
            var commandDescriptions = new Dictionary<string, string>
            {
                { "start_tournament", "Start a new tournament." },
                { "end_tournament", "End the current tournament." },
                { "time_tournament", "Check the remaining tournament time." },
                { "enter_tournament", "Join the tournament." },
                { "exit_tournament", "Leave the tournament." },
                { "status_tournament", "View the leaderboard. Use '/status_tournament [page]' for additional pages." },
                { "score_tournament", "Display the scores for all participants." },
                { "reload_config", "Reload the tournament configuration." },
                { "show_rules", "View the tournament scoring rules." }
            };

            // Define restricted commands for non-admins
            var restrictedCommands = new HashSet<string>
            {
                "start_tournament",
                "end_tournament",
                "reload_config",
                "show_rules"
            };

            // Determine available commands based on permissions
            var availableCommands = commandDescriptions.Keys
                .Where(cmd => !restrictedCommands.Contains(cmd) || HasAdminPermission(player))
                .ToList();

            // Build the help text dynamically
            var helpText = "Available Commands:\n";
            foreach (var commandName in availableCommands)
            {
                if (commandDescriptions.TryGetValue(commandName, out var description))
                {
                    helpText += $"- /{commandName}: {description}\n";
                }
            }

            // Send the help text to the player
            SendPlayerMessage(player, helpText.TrimEnd());
        }

        #endregion
    }
}
