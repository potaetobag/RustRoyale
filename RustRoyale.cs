using System;
using System.Collections.Generic;
using System.IO;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using Rust;

namespace Oxide.Plugins
{
    [Info("RustRoyale", "Potaetobag", "1.1.2"), Description("Rust Royale custom tournament game mode with point-based scoring system.")]
    class RustRoyale : RustPlugin
    {
    
    #region Configuration
        private ConfigData Configuration;

        private class ConfigData
        {
            public string DiscordWebhookUrl { get; set; } = "";
            public string ChatFormat { get; set; } = "[<color=#d97559>RustRoyale</color>] {message}";
            public string ChatIconSteamId { get; set; } = "76561199815164411";
            public string ChatUsername { get; set; } = "[RustRoyale]";
            public string Timezone { get; set; } = "UTC";
            public string StartDay { get; set; } = "Friday";
            public bool AutoStartEnabled { get; set; } = true;
            public bool AutoEnrollEnabled { get; set; } = true;
            public int StartHour { get; set; } = 14;
            public int StartMinute { get; set; } = 0;
            public int DurationHours { get; set; } = 72;
            public int DataRetentionDays { get; set; } = 30; // Default to 30 days
            public int TopPlayersToTrack { get; set; } = 3; // Default to Top 3 players
            public List<int> NotificationIntervals { get; set; } = new List<int> { 600, 60 }; // Default: every 10 minutes (600 seconds) and last minute (60 seconds)
            public Dictionary<string, int> ScoreRules { get; set; } = new Dictionary<string, int>
            {
                {"KILL", 3},     // Human player kills another human player
                {"DEAD", -3},    // Human player is killed by another human player
                {"JOKE", -1},    // Death by traps, self-inflicted damage
                {"NPC", 1},      // Kill an NPC (Murderer, Zombie, Scientist, Scarecrow)
                {"ENT", 5},      // Kill a Helicopter or Bradley Tank
                {"BRUH", -2},     // Death by an NPC, Helicopter, or Bradley
                {"WHY", 1}       // NEW: Award 1 point for killing an animal (wolf, boar, bear, stag, deer) from over Xm away
            };
            public float AnimalKillDistance { get; set; } = 100f;
            public Dictionary<string, string> MessageTemplates { get; set; } = new Dictionary<string, string>
            {
                {"StartTournament", "Brace yourselves, champions! The tournament has begun! Time to show off those pro skills (or hilarious fails). Time left: {TimeRemaining}. Duration: {Duration} hours."},
                {"EndTournament", "The tournament is over! Congrats to the winners, and for the rest... better luck next time (maybe practice a bit?)."},
                {"PlayerScoreUpdate", "{PlayerName} just bagged {Score} point{PluralS} for {Action}. Somebody's on fire!"},
                {"TopPlayers", "Leaderboard time! Top {Count} players are: {PlayerList}. Did your name make the cut, or are you just here for fun?"},
                {"TimeRemaining", "Tick-tock! Time remaining in the tournament: {Time}. Don't waste it—score some points!"},
                {"JoinTournament", "{PlayerName} has entered the fray! Grab the popcorn, this should be good."},
                {"LeaveTournament", "{PlayerName} has exited the battlefield. Maybe they got scared? We’ll never know."},
                
                {"KillPlayerWithEntity", "{PlayerName} earned {Score} point{PluralS} for eliminating {VictimName} with {EntityName} to respawn land! Total score: {TotalScore}. Savage!"},
                {"SelfInflictedDeath", "Oops! {PlayerName} lost {Score} point{PluralS} for a self-inflicted oopsie. Total score: {TotalScore}. Smooth move, buddy."},
                {"DeathByEntity", "{PlayerName} was defeated by {AttackerType} and lost {Score} point{PluralS}. Ouch! Total score: {TotalScore}"},
                {"DeathByNPC", "Yikes! {PlayerName} lost {Score} point{PluralS} for getting clobbered by an NPC. Total score: {TotalScore}."},
                {"KillEntity", "{PlayerName} earned {Score} point{PluralS} for obliterating a {AttackerType}! Total score: {TotalScore}. BOOM!"},
                {"KillNPC", "{PlayerName} earned {Score} point{PluralS} for bravely taking down an NPC! Total score: {TotalScore}."},
                {"KillPlayer", "{PlayerName} earned {Score} point{PluralS} for sending {VictimName} to respawn land! Total score: {TotalScore}."},
                {"KilledByPlayer", "{VictimName} lost {Score} point{PluralS} for being killed by {AttackerName}. Total score: {TotalScore}. Better luck next time!"},
                {"DeathByBRUH", "{PlayerName} lost {Score} point{PluralS} for getting defeated by {EntityName}. Total score: {TotalScore}. BRUH moment!"},
                {"KillAnimal", "{PlayerName} earned {Score} point{PluralS} for killing an animal ({VictimName}) from over {Distance} meters away! Total score: {TotalScore}."},
                {"NoTournamentRunning", "Hold your horses! There's no tournament right now. Next round starts in {TimeRemainingToStart}. Grab a snack meanwhile!"},
                {"ParticipantsAndScores", "Scoreboard time! (Page {Page}/{TotalPages}): {PlayerList}. Who’s crushing it? Who’s just chilling?"},
                {"NotInTournament", "Uh-oh! You’re not part of the tournament. Join in, don’t be shy!"},
                {"NoPermission", "Sorry, you don’t have permission to {ActionName}. Maybe ask the admins for a favor?"},
                {"AlreadyParticipating", "Relax, {PlayerName}. You’re already in the tournament. No need to double-dip!"},
                {"AlreadyOptedIn", "Nice try, {PlayerName}, but you’re already opted in. Eager much?"},
                {"OptedOutTournament", "{PlayerName} has decided to opt out. Bye-bye! Don’t let FOMO get you."},
                {"NotOptedInTournament", "You weren’t even opted in, {PlayerName}. Why so dramatic?"},
                {"TournamentNotRunning", "Patience is a virtue, {PlayerName}. No tournament now. Next round starts in {TimeRemainingToStart}. Go sharpen your skills!"},
                {"TournamentScores", "Here’s the rundown of scores: {PlayerList}. Is your name shining, or are you just here for the jokes?"},
                {"TournamentAlreadyRunning", "Whoa there! A tournament is already underway. Time left: {TimeRemaining}. Jump in or cheer from the sidelines!"},
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

            // Validate StartMinute
            if (Configuration.StartMinute < 0 || Configuration.StartMinute > 59)
            {
                PrintWarning("Invalid StartMinute in configuration. Defaulting to 0 (top of the hour).");
                Configuration.StartMinute = 0;
                updated = true;
            }

            // Validate DurationHours
            if (Configuration.DurationHours < 0.0167 || Configuration.DurationHours > 168) // Limit to 1 week
            {
                PrintWarning($"Invalid DurationHours in configuration: {Configuration.DurationHours}. Defaulting to 72 hours.");
                Configuration.DurationHours = 72;
                updated = true;
            }
            else if (Configuration.DurationHours < 1 && Configuration.DurationHours >= 0.0167)
            {
                Puts($"[Debug] Short tournament duration detected: {Configuration.DurationHours} hours. This is valid.");
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
            ValidateConfiguration();
            LoadAutoEnrollBlacklist();
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
        private string currentTournamentFile; // Stores the current tournament file path

        private void EnsureDataDirectory()
        {
            if (!Directory.Exists(DataDirectory))
            {
                Directory.CreateDirectory(DataDirectory);
                LogMessage($"Created data directory: {DataDirectory}");
            }
        }

        private void StartTournamentFile()
        {
            EnsureDataDirectory();

            // Generate the file name once at the start of the tournament
            if (string.IsNullOrEmpty(currentTournamentFile))
            {
                currentTournamentFile = $"{DataDirectory}/Tournament_{DateTime.UtcNow:yyyyMMddHHmmss}.data";
                LogMessage($"Created new tournament log file: {currentTournamentFile}");
            }
        }

        private void LogEvent(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(currentTournamentFile))
                {
                    PrintWarning("Tournament file has not been initialized. Call StartTournamentFile() first.");
                    return;
                    StartTournamentFile();
                }

                File.AppendAllText(currentTournamentFile, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} - {message}\n");
            }
            catch (Exception ex)
            {
                PrintError($"Unexpected error while logging event: {ex.Message}\n{ex.StackTrace}");
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
                            LogMessage($"Deleted old tournament file: {file}");
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

            if (string.IsNullOrEmpty(currentTournamentFile))
            {
                PrintWarning("Tournament file has not been initialized. Call StartTournamentFile() first.");
                return;
            }

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

            if (timeUntilTarget.TotalSeconds <= 1.555)
            {
                Puts($"[Debug] Timer expired immediately for target: {targetTime}. Invoking completion.");
                if (onCompletion != null)
                {
                    try
                    {
                        Puts("[Debug] Immediate invocation of onCompletion due to expired timer.");
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

                NotifyTournamentCountdown(remainingTime);

                if (remainingTime.TotalSeconds <= 1.555)
                {
                    Puts("[Debug] Countdown timer expired; invoking completion.");
                    countdownTimer?.Destroy();
                    countdownTimer = null;

                    if (onCompletion != null)
                    {
                        try
                        {
                            Puts("[Debug] Invoking onCompletion callback after timer expiration.");
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

                // Puts($"[Debug] Timer tick. Remaining time: {remainingTime.TotalSeconds}s.");
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

                DateTime nextStart = localNow
                .AddDays((7 + (int)startDay - (int)localNow.DayOfWeek) % 7)
                .Date
                .AddHours(Configuration.StartHour)
                .AddMinutes(Configuration.StartMinute);

            if (nextStart <= localNow)
            {
                nextStart = nextStart.AddDays(7);
            }

                tournamentStartTime = GetUtcTime(nextStart);
                LogEvent($"Tournament scheduled to start on {GetLocalTime(tournamentStartTime):yyyy-MM-dd HH:mm:ss} ({Configuration.Timezone}).");

                ScheduleCountdown(tournamentStartTime, () =>
                {
                    Puts("[Debug] Timer expired, attempting to start the tournament.");
                    if (!isTournamentRunning)
                    {
                        StartTournament();
                    }
                    else
                    {
                        Puts("[Debug] Tournament is already running. Skipping StartTournament.");
                    }
                });
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
            int remainingSeconds = (int)Math.Floor(remainingTime.TotalSeconds);

            if (Configuration.NotificationIntervals.Contains(remainingSeconds) && lastNotifiedMinutes != remainingSeconds)
            {
                lastNotifiedMinutes = remainingSeconds;

                string formattedTime = FormatTimeRemaining(remainingTime);
                Puts($"[Debug] Sending tournament countdown message: {formattedTime}");

                // Send a global chat message
                string globalMessage = FormatMessage(Configuration.MessageTemplates["TournamentCountdown"], new Dictionary<string, string>
                {
                    { "Time", formattedTime }
                });
                SendTournamentMessage(globalMessage);

                Puts($"[Debug] Tournament countdown message sent to global chat: {globalMessage}");

                // Notify players individually (if needed)
                Notify("TournamentCountdown", null, placeholders: new Dictionary<string, string>
                {
                    { "Time", formattedTime }
                });

                // Send a Discord message if configured
                if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
                {
                    string discordMessage = FormatMessage(Configuration.MessageTemplates["TournamentCountdown"], new Dictionary<string, string>
                    {
                        { "Time", formattedTime }
                    });
                    SendDiscordMessage(discordMessage);

                    Puts($"[Debug] Discord countdown message sent: {discordMessage}");
                }
            }
            else
            {
                Puts($"[Debug] No notification sent for {remainingSeconds}s remaining. Last notified: {lastNotifiedMinutes}s");
            }
        }

        private void NotifyTournamentStartImminent()
        {
            string timeRemaining = FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow);

            // Notify chat
            Notify("TournamentAboutToStart", null, placeholders: new Dictionary<string, string>
            {
                { "Time", timeRemaining }
            });

            // Notify Discord
            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                string discordMessage = $"The tournament is about to start in {timeRemaining}. Don't miss it!";
                SendDiscordMessage(discordMessage);
                Puts($"[Debug] Sending imminent start notification to Discord: {discordMessage}");
            }
        }

        private Timer tournamentDurationTimer;

        private void ScheduleTournamentEnd()
        {
            TimeSpan tournamentDuration = TimeSpan.FromHours(Configuration.DurationHours);
            Puts($"[Debug] Scheduling tournament end in {tournamentDuration.TotalSeconds} seconds.");

            tournamentDurationTimer?.Destroy();

            tournamentDurationTimer = timer.Once((float)tournamentDuration.TotalSeconds, () =>
            {
                Puts("[Debug] Tournament duration timer expired. Ending tournament...");
                EndTournament();
            });

            // Optional debug: track remaining time every minute
            timer.Every(60f, () =>
            {
                if (!isTournamentRunning || tournamentDurationTimer == null)
                {
                    Puts("[Debug] Tournament is not running or duration timer has been destroyed. Stopping debug timer.");
                    return;
                }

                TimeSpan remainingTime = tournamentEndTime - DateTime.UtcNow;
                // Puts($"[Debug] Tournament countdown: {FormatTimeRemaining(remainingTime)} remaining.");
            });
        }

    #endregion

    #region Tournament Logic
        [ChatCommand("start_tournament")]
        private void StartTournamentCommand(BasePlayer player, string command, string[] args)
        {
            // Log the command initiation by the player
            Puts($"[Debug] Player {player.displayName} ({player.UserIDString}) issued the /start_tournament command.");

            if (!ValidateAdminCommand(player, "start the tournament"))
            {
                Puts($"[Debug] Player {player.displayName} ({player.UserIDString}) lacks the required admin permissions.");
                return;
            }

            // Check if the tournament is already running
            if (isTournamentRunning)
            {
                Puts("[Debug] Attempt to start tournament while one is already running.");

                // Log the remaining time for the current tournament
                Puts($"[Debug] Time remaining in the current tournament: {FormatTimeRemaining(tournamentEndTime - DateTime.UtcNow)}");

                string message = FormatMessage(Configuration.MessageTemplates["TournamentAlreadyRunning"], new Dictionary<string, string>
                {
                    { "TimeRemaining", FormatTimeRemaining(tournamentEndTime - DateTime.UtcNow) }
                });
                SendPlayerMessage(player, message);
                return;
            }

            Puts($"[Debug] Tournament is not running. Proceeding to start the tournament as requested by {player.displayName} ({player.UserIDString}).");

            StartTournament();

            // Log the success of the tournament start
            Puts($"[Debug] Tournament started successfully by {player.displayName} ({player.UserIDString}).");
        }

        private void StartTournament()
        {
            Puts("[Debug] StartTournament invoked.");

            StartTournamentFile();

            try
            {
                Puts($"[Debug] Current state: isTournamentRunning={isTournamentRunning}, tournamentEndTime={tournamentEndTime:yyyy-MM-dd HH:mm:ss} UTC.");

                if (isTournamentRunning)
                {
                    Puts("[Debug] Attempted to start tournament, but one is already running.");
                    return;
                }

                if (Configuration.DurationHours <= 0.0167)
                {
                    PrintError("[Error] Invalid tournament duration. Ensure `DurationHours` in the configuration is greater than 0.");
                    return;
                }

                isTournamentRunning = true;
                tournamentEndTime = DateTime.UtcNow.AddHours(Configuration.DurationHours);
                Puts($"[Info] Tournament started. Duration: {Configuration.DurationHours} hours. Ends at: {tournamentEndTime:yyyy-MM-dd HH:mm:ss} UTC.");

                playerStats.Clear();
                foreach (var participant in participantsData.Values)
                {
                    participant.Score = 0;
                }

                if (countdownTimer != null)
                {
                    Puts("[Debug] Destroying existing countdown timer.");
                    countdownTimer.Destroy();
                    countdownTimer = null;
                }

                if (participantsData.Values.Any())
                {
                    LogEvent($"[Info] Participants at tournament start: {string.Join(", ", participantsData.Values.Select(p => p.Name))}");
                }
                else
                {
                    LogEvent("[Info] No participants at tournament start.");
                }

                LogEvent("[Info] Tournament started successfully.");

                // Start the tournament duration timer
                ScheduleTournamentEnd();
                
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
                    Puts($"[Debug] Sending Discord notification for tournament start.");
                    SendDiscordMessage(globalMessage); // Discord notification
                }

            }
            catch (Exception ex)
            {
                PrintError($"[Error] Failed to start tournament: {ex.Message}");
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
        }

        private void EndTournament()
        {
            Puts("[Debug] EndTournament invoked.");

            try
            {
                if (!isTournamentRunning)
                {
                    Puts("[Debug] No tournament is currently running.");
                    return;
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

                // Prepare leaderboard/results
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

                Puts($"[Debug] Tournament successfully ended. Total participants: {sortedParticipants.Count}");

                // Reset stats for participants AFTER leaderboard notifications
                foreach (var participant in participantsData.Values)
                {
                    participant.Score = 0;
                }

                // Cleanup timers if active
                countdownTimer?.Destroy();
                countdownTimer = null;

                // Schedule the next tournament
                Puts("[Debug] Scheduling the next tournament.");
                ScheduleTournament();
            }
            catch (Exception ex)
            {
                // Log any errors during the end of the tournament
                PrintError($"Failed to end tournament: {ex.Message}");
            }
        }

        private void OnPlayerInit(BasePlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.displayName))
                return;

            playerNameCache[player.userID] = player.displayName;

            if (participantsData.TryGetValue(player.userID, out var participant) && participant.Name == "Unknown")
            {
                participant.Name = player.displayName;
                SaveParticipantsData();
                Puts($"Updated participant data: {player.userID} now has name '{player.displayName}'.");
            }

            if (Configuration.AutoEnrollEnabled && !autoEnrollBlacklist.Contains(player.userID) && !participantsData.ContainsKey(player.userID))
            {
                participantsData.TryAdd(player.userID, new PlayerStats(player.userID));
                SaveParticipantsData();
                SendPlayerMessage(player, "You have been automatically enrolled into the RustRoyale tournament system, you can opt out by entering /close_tournament.");
                Puts($"[Debug] Automatically enrolled player: {player.displayName} ({player.userID})");
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player != null)
            {
                playerNameCache.TryRemove(player.userID, out _);
            }
        }

        private readonly ConcurrentDictionary<ulong, DateTime> recentDeaths = new ConcurrentDictionary<ulong, DateTime>();
        private const int RecentDeathWindowSeconds = 5;

		private void OnPlayerDeath(BasePlayer victim, HitInfo info)
		{
			if (!isTournamentRunning || victim == null)
			{
				Puts("[Debug] Tournament is not running or victim is null.");
				return;
			}

			// Prevent duplicate death processing
			if (recentDeaths.TryGetValue(victim.userID, out var lastDeathTime) &&
				(DateTime.UtcNow - lastDeathTime).TotalSeconds < RecentDeathWindowSeconds)
			{
				Puts($"[Debug] Ignoring duplicate death event for {victim.displayName}. Last death: {lastDeathTime}.");
				return;
			}

			// Record the timestamp of the death
			recentDeaths[victim.userID] = DateTime.UtcNow;

			var attacker = info?.InitiatorPlayer;
			var initiatorEntity = info?.Initiator as BaseCombatEntity;

			string attackerName = attacker?.displayName ?? "Unknown";
			string entityName = initiatorEntity?.ShortPrefabName ?? "Unknown";
			ulong ownerId = initiatorEntity?.OwnerID ?? 0;
			string ownerName = ownerId != 0 ? GetPlayerName(ownerId) : "None";
            
            bool IsAnimalKill(string prefabName)
            {
                var animals = new List<string> { "wolf", "boar", "bear", "stag", "deer" };
                return animals.Contains(prefabName.ToLower());
            }

			Puts($"[Debug] Death event detected: Victim={victim.displayName} (Type={victim.ShortPrefabName}), Attacker={attackerName}, Entity={entityName}, Owner={ownerName}.");

            // Handle deaths caused by helicopters or Bradley tanks
            if (initiatorEntity != null && participants.Contains(victim.userID))
            {
                string entityType = null;

                // Ensure initiatorEntity's prefab name is checked
                if (entityName.Contains("helicopter", StringComparison.OrdinalIgnoreCase))
                {
                    entityType = "Helicopter";
                }
                else if (entityName.Contains("bradley", StringComparison.OrdinalIgnoreCase))
                {
                    entityType = "Bradley";
                }

                if (!string.IsNullOrEmpty(entityType))
                {
                    DamageType? majorityDamageType = info?.damageTypes?.GetMajorityDamageType();
                    string damageTypeString = majorityDamageType.HasValue ? majorityDamageType.Value.ToString() : "Unknown";

                    Puts($"[Debug] Helicopter/Bradley kill detected: Victim={victim.displayName}, Entity={entityType}, DamageType={damageTypeString}, EntityName={entityName}");

                    if (Configuration.ScoreRules.TryGetValue("BRUH", out int points))
                    {
                        UpdatePlayerScore(
                            victim.userID,
                            "BRUH",
                            $"getting defeated by a {entityType}",
                            victim,
                            info,
                            entityName: entityType
                        );

                        Puts($"[Debug] {victim.displayName} lost points for being defeated by a {entityType}. Total score updated.");
                    }

                    return;
                }
                else
                {
                    DamageType? majorityDamageType = info?.damageTypes?.GetMajorityDamageType();
                    string damageTypeString = majorityDamageType.HasValue ? majorityDamageType.Value.ToString() : "Unknown";

                    Puts($"[Debug] Unhandled entity type in Helicopter/Bradley check. Victim={victim.displayName}, EntityName={entityName ?? "Unknown"}, DamageType={damageTypeString}");
                }
            }
			
			    Puts($"[Debug] ❌ ENT Kills Player Handler was skipped. Victim: {victim.ShortPrefabName}, IsNpc={victim.IsNpc}, Attacker={attacker?.displayName ?? "None"}");

            // Handle deaths caused by NPCs or unowned entities (BRUH)
            if (
					(attacker != null && attacker.IsNpc) 
					|| (initiatorEntity != null && ownerId == 0 && (attacker == null || attacker.IsNpc))
				)
            {
                if (attacker != null && !string.IsNullOrEmpty(attacker.ShortPrefabName))
                {
                    attackerName = attacker.ShortPrefabName; // Assign proper NPC name
                }
                else if (initiatorEntity is BaseNpc npc && !string.IsNullOrEmpty(npc.ShortPrefabName))
                {
                    attackerName = npc.ShortPrefabName; // Assign proper NPC name
                }
                else if (initiatorEntity != null && string.IsNullOrEmpty(attackerName))
                {
                    attackerName = initiatorEntity.ShortPrefabName; // Use initiator's prefab name as fallback
                }

                if (participants.Contains(victim.userID) && Configuration.ScoreRules.TryGetValue("BRUH", out int points))
                {
                    UpdatePlayerScore(
                        victim.userID,
                        "BRUH",
                        $"being defeated by {attackerName}",
                        victim,
                        info,
                        entityName: attackerName
                    );

                    Puts($"[Debug] {victim.displayName} lost points for being defeated by {attackerName}.");
                }

                return;
            }
			
			    Puts($"[Debug] ❌ NPC Kills Player Handler was skipped. Victim: {victim.ShortPrefabName}, IsNpc={victim.IsNpc}, Attacker={attacker?.displayName ?? "None"}");

            // Handle deaths caused by traps, turrets, or sentries
            if (initiatorEntity != null && participants.Contains(victim.userID))
            {
                string friendlyEntityName = entityName switch
                {
                    "autoturret_deployed" => "autoturret",
                    "guntrap" => "guntrap",
                    "flameturret.deployed" => "flameturret",
                    _ => entityName
                };

                Puts($"[Debug] Trap/Turret kill detected: Entity={friendlyEntityName}, OwnerID={ownerId}, OwnerName={ownerName}");

                if (ownerId != 0 && participants.Contains(ownerId))
                {
                    if (Configuration.ScoreRules.TryGetValue("KILL", out int pointsForOwner) &&
                        Configuration.ScoreRules.TryGetValue("DEAD", out int pointsForVictim))
                    {
                        UpdatePlayerScore(
                            victim.userID,
                            "DEAD",
                            $"being killed by {friendlyEntityName} owned by {ownerName}",
                            victim,
                            info,
                            attackerName: ownerName,
                            entityName: friendlyEntityName
                        );

                        UpdatePlayerScore(
                            ownerId,
                            "KILL",
                            $"eliminating {victim.displayName} with {friendlyEntityName}",
                            victim,
                            info,
                            attackerName: ownerName,
                            entityName: friendlyEntityName,
                            reverseMessage: true
                        );

                        Puts($"[Debug] {victim.displayName} lost points for being killed by {friendlyEntityName} owned by {ownerName}.");
                        Puts($"[Debug] {ownerName} gained points for killing {victim.displayName} with {friendlyEntityName}.");
                    }
                }
                else
                {
                    if (Configuration.ScoreRules.TryGetValue("JOKE", out int jokePoints))
                    {
                        UpdatePlayerScore(
                            victim.userID,
                            "JOKE",
                            $"death caused by an unowned {friendlyEntityName}",
                            victim,
                            info,
                            entityName: friendlyEntityName
                        );

                        Puts($"[Debug] {victim.displayName} died due to an unowned {friendlyEntityName}.");
                    }
                }

                return;
            }
			
			    Puts($"[Debug] ❌ Death By Traps Handler was skipped. Victim: {victim.ShortPrefabName}, IsNpc={victim.IsNpc}, Attacker={attacker?.displayName ?? "None"}");

            // Handle Entity eliminated by player
            if (initiatorEntity != null && attacker != null && !victim.IsNpc && participants.Contains(attacker.userID))
            {
                if (Configuration.ScoreRules.TryGetValue("ENT", out int points))
                {
                    UpdatePlayerScore(
                        attacker.userID,
                        "ENT",
                        $"eliminating an entity ({victim.ShortPrefabName})",
                        victim,
                        info
                    );

                    Puts($"[Debug] {attacker.displayName} earned points for killing an entity ({victim.ShortPrefabName}).");
                }
                return;
            }
			
			    Puts($"[Debug] ❌ ENT Killed By Player Handler was skipped. Victim: {victim.ShortPrefabName}, IsNpc={victim.IsNpc}, Attacker={attacker?.displayName ?? "None"}");
			
            // Handle Animals killed by player
            if (victim.IsNpc && attacker != null && IsAnimalKill(victim.ShortPrefabName))
            {
                // Calculate the distance between attacker and victim
                float distance = Vector3.Distance(attacker.transform.position, victim.transform.position);
                // Use the configurable threshold from your configuration file
                if (distance > Configuration.AnimalKillDistance)
                {
                    if (participants.Contains(attacker.userID) && Configuration.ScoreRules.TryGetValue("WHY", out int points))
                    {
                        Puts($"[Debug] Awarding {points} point for killing an animal ({victim.ShortPrefabName}) from {distance:F1} meters away to {attacker.displayName}.");
                        UpdatePlayerScore(
                            attacker.userID,
                            "WHY",
                            $"killing an animal ({victim.ShortPrefabName}) from over {Configuration.AnimalKillDistance} meters away",
                            victim,
                            info
                        );
                    }
                    // Prevent further scoring for this event by returning immediately
                    return;
                }
            }

                Puts($"[Debug] ❌ Animal Killed By Player Handler was skipped. Victim: {victim.ShortPrefabName}, IsNpc={victim.IsNpc}, Attacker={attacker?.displayName ?? "None"}");
            
			// Handle NPCs killed by player
			if (victim.IsNpc && attacker != null)
			{
				bool isParticipant = participants.Contains(attacker.userID);
				bool hasScoreRule = Configuration.ScoreRules.TryGetValue("NPC", out int points);

				Puts($"[Debug] Checking NPC kill: Victim={victim.ShortPrefabName}, Attacker={attacker.displayName}, In Tournament={isParticipant}, Score Rule Exists={hasScoreRule}");
				Puts($"[Debug] Available ScoreRules: {string.Join(", ", Configuration.ScoreRules.Keys)}");

				if (isParticipant)
				{
					if (hasScoreRule)
					{
						Puts($"[Debug] Awarding {points} points for killing an NPC ({victim.ShortPrefabName}) to {attacker.displayName}.");

						UpdatePlayerScore(
							attacker.userID,
							"NPC",
							$"eliminating an NPC ({victim.ShortPrefabName})",
							victim,
							info
						);

						Puts($"[Debug] Score should now be updated for {attacker.displayName}.");
					}
					else
					{
						Puts("[Debug] No score rule found for NPC kills.");
					}
				}
				else
				{
					Puts($"[Debug] {attacker.displayName} is not a tournament participant, skipping scoring.");
				}
				return;
			}
			
			    Puts($"[Debug] ❌ NPC Killed By Player Handler was skipped. Victim: {victim.ShortPrefabName}, IsNpc={victim.IsNpc}, Attacker={attacker?.displayName ?? "None"}");

            // Handle NPCs killed by player-owned turrets
            if (initiatorEntity != null && victim.IsNpc && ownerId != 0 && participants.Contains(ownerId))
            {
                string friendlyEntityName = entityName switch
                {
                    "autoturret_deployed" => "autoturret",
                    "guntrap" => "guntrap",
                    "flameturret.deployed" => "flameturret",
                    _ => entityName
                };

                Puts($"[Debug] NPC kill detected: Victim={victim.displayName} (NPC), Entity={friendlyEntityName}, Owner={ownerName}");

                if (Configuration.ScoreRules.TryGetValue("NPC", out int pointsForOwner))
                {
                    UpdatePlayerScore(
                        ownerId,
                        "NPC",
                        $"eliminating an NPC ({victim.ShortPrefabName}) with {friendlyEntityName}",
                        victim,
                        info,
                        attackerName: ownerName,
                        entityName: friendlyEntityName
                    );

                    Puts($"[Debug] {ownerName} earned points for killing an NPC ({victim.ShortPrefabName}) with {friendlyEntityName}.");
                }

                return;
            }
			
			    Puts($"[Debug] ❌ NPC KilledBy Player Traps Handler was skipped. Victim: {victim.ShortPrefabName}, IsNpc={victim.IsNpc}, Attacker={attacker?.displayName ?? "None"}");

            // Handle self-inflicted deaths (e.g., falling, drowning, explosions)
            if ((attacker == null || attacker == victim) && participants.Contains(victim.userID))
            {
                string cause = info?.damageTypes?.GetMajorityDamageType().ToString() ?? "unknown cause";

                if (Configuration.ScoreRules.TryGetValue("JOKE", out int points))
                {
                    UpdatePlayerScore(
                        victim.userID,
                        "JOKE",
                        $"self-inflicted death ({cause})",
                        victim,
                        info
                    );

                    Puts($"[Debug] {victim.displayName} died from {cause} (self-inflicted).");
                }

                return;
            }

            // Handle player kills another player
            if (attacker != null && participants.Contains(attacker.userID) && participants.Contains(victim.userID))
            {
                if (Configuration.ScoreRules.TryGetValue("DEAD", out int pointsForVictim) &&
                    Configuration.ScoreRules.TryGetValue("KILL", out int pointsForAttacker))
                {
                    UpdatePlayerScore(victim.userID, "DEAD", $"killed by {attackerName}", victim);
                    UpdatePlayerScore(attacker.userID, "KILL", $"eliminated {victim.displayName}", victim);
                    Puts($"[Debug] {attackerName} killed {victim.displayName}.");
                }

                return;
            }
			
			    Puts($"[Debug] ❌ Player Kills Player Handler was skipped. Victim: {victim.ShortPrefabName}, IsNpc={victim.IsNpc}, Attacker={attacker?.displayName ?? "None"}");

        }

    #endregion

    #region Score Handling

        private string GetPlayerName(ulong userId)
        {
            // 1. Check the cache first
            if (playerNameCache.TryGetValue(userId, out var cachedName) && !string.IsNullOrEmpty(cachedName) && cachedName != "Unknown")
            {
                return cachedName;
            }

            // 2. Try to retrieve the player if they are online or sleeping
            var player = BasePlayer.FindByID(userId) ?? BasePlayer.FindSleeping(userId);
            if (player != null && !string.IsNullOrEmpty(player.displayName))
            {
                playerNameCache[userId] = player.displayName;

                // Update the participantsData if necessary
                if (participantsData.TryGetValue(userId, out var participant) && participant.Name == "Unknown")
                {
                    participant.Name = player.displayName;
                    SaveParticipantsData();
                }

                return player.displayName;
            }

            // 3. Check participantsData for the player's name
            if (participantsData.TryGetValue(userId, out var participantFromData) && !string.IsNullOrEmpty(participantFromData.Name) && participantFromData.Name != "Unknown")
            {
                playerNameCache[userId] = participantFromData.Name;
                return participantFromData.Name;
            }

            // 4. Load from the Participants.json file if necessary
            try
            {
                if (File.Exists(ParticipantsFile))
                {
                    var participantsFromFile = JsonConvert.DeserializeObject<Dictionary<ulong, PlayerStats>>(File.ReadAllText(ParticipantsFile));
                    if (participantsFromFile != null && participantsFromFile.TryGetValue(userId, out var participantFromFile))
                    {
                        // Update the cache and participantsData dynamically
                        if (!string.IsNullOrEmpty(participantFromFile.Name) && participantFromFile.Name != "Unknown")
                        {
                            playerNameCache[userId] = participantFromFile.Name;

                            // Synchronize participantsData with file
                            if (participantsData.TryGetValue(userId, out var participant) && participant.Name == "Unknown")
                            {
                                participant.Name = participantFromFile.Name;
                                SaveParticipantsData();
                            }

                            return participantFromFile.Name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to retrieve player name from Participants.json: {ex.Message}");
            }

            // 5. Fallback: If the name is still "Unknown," log and attempt to update later
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
				EnsureDataDirectory(); // Ensure directory exists before writing

				string serializedData;
				lock (participantsDataLock)
				{
					serializedData = SerializeParticipantsData(participantsData);
				}

				// Debug Log: Before writing to file
				Puts($"[Debug] Preparing to save Participants.json. Data:\n{serializedData}");

				// Ensure the file is accessible before writing
				if (File.Exists(ParticipantsFile))
				{
					try
					{
						using (FileStream fs = File.Open(ParticipantsFile, FileMode.Open, FileAccess.Read, FileShare.None))
						{
							Puts("[Debug] Participants.json is not in use. Proceeding with write.");
						}
					}
					catch (IOException)
					{
						PrintWarning("[Warning] Participants.json is currently in use. Save operation skipped.");
						return;
					}
				}

				// Write the updated data to the file inside a lock
				lock (participantsDataLock)
				{
					File.WriteAllText(ParticipantsFile, serializedData);
				}

				// Verify data was successfully written
				string savedData = File.ReadAllText(ParticipantsFile);
				Puts($"[Debug] Successfully saved Participants.json. Contents:\n{savedData}");
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

        private void UpdatePlayerScore(ulong userId, string actionCode, string actionDescription, BasePlayer victim = null, HitInfo info = null, string attackerName = null, string entityName = "Unknown", bool reverseMessage = false)
		{
			// Debug start of function call
			Puts($"[Debug] UpdatePlayerScore called for UserID={userId}, ActionCode={actionCode}, Victim={victim?.displayName ?? "None"}, Entity={entityName}");

			// Ensure player is in participantsData
			if (!participantsData.TryGetValue(userId, out var participant))
			{
				PrintWarning($"[Error] Player with UserID {userId} not found in participants. Current participants: {string.Join(", ", participantsData.Keys)}");
				return;
			}

			// Verify the actionCode exists in the ScoreRules
			if (!Configuration.ScoreRules.TryGetValue(actionCode, out int points))
			{
				PrintWarning($"[Error] Invalid action code: {actionCode}. Available codes: {string.Join(", ", Configuration.ScoreRules.Keys)}");
				return;
			}

			// Store previous score before update
			int previousScore = participant.Score;
			participant.Score += points;

			// Debug updated score before saving
			Puts($"[Debug] {participant.Name} (UserID: {userId}) | Previous Score: {previousScore} | Gained: {points} | New Score: {participant.Score}");

			// Save updated scores and confirm write success
			SaveParticipantsData();
			
			// Debug participants.json file after save
			string savedData = File.ReadAllText(ParticipantsFile);
			Puts($"[Debug] Participants.json after save: {savedData}");

			// Format pluralization
			string pluralS = Math.Abs(points) == 1 ? "" : "s";

			// Determine message template key
			string templateKey = reverseMessage ? "KillPlayerWithEntity" : actionCode switch
			{
				"KILL" => "KillPlayerWithEntity",
				"DEAD" => "KilledByPlayer",
				"JOKE" => "SelfInflictedDeath",
				"NPC" => "KillNPC",
				"ENT" => "KillEntity",
				"BRUH" => "DeathByBRUH",
                "WHY"  => "KillAnimal",
				_ => "PlayerScoreUpdate"
			};

			// Fetch the message template
			if (!Configuration.MessageTemplates.TryGetValue(templateKey, out string template))
			{
				PrintWarning($"[Error] Message template for action code '{actionCode}' not found. Using default.");
				template = Configuration.MessageTemplates["PlayerScoreUpdate"];
			}

			// Ensure attackerName is set
			attackerName ??= GetPlayerName(userId);

			// Set placeholders
			var placeholders = new Dictionary<string, string>
			{
				{ "PlayerName", GetPlayerName(userId) },
				{ "VictimName", victim?.displayName ?? "Unknown" },
				{ "AttackerName", attackerName },
				{ "EntityName", entityName },
				{ "Score", points.ToString() },
				{ "TotalScore", participant.Score.ToString() },
				{ "Action", actionDescription },
				{ "PluralS", pluralS }
			};

            // For animal kills, include the distance placeholder
            if (actionCode == "WHY")
            {
                placeholders["Distance"] = Configuration.AnimalKillDistance.ToString();
            }

			// Generate final message
			string globalMessage = FormatMessage(template, placeholders);
			
			// Send messages
			SendTournamentMessage(globalMessage);
			if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
			{
				SendDiscordMessage(globalMessage);
			}

			// Log event
			LogEvent($"[Event] {participant.Name} received {points} point{pluralS} for {actionDescription}. New total score: {participant.Score}.");
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
            public string Name { get; set; } // Add a setter to allow updates to the Name
            public int Score { get; set; }

            public PlayerStats(ulong userId)
            {
                UserId = userId;
                Name = "Unknown"; // Default to "Unknown"
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

    #region Auto Enroll Handling

        private HashSet<ulong> autoEnrollBlacklist = new HashSet<ulong>();
        private string AutoEnrollBlacklistFile => $"{DataDirectory}/AutoEnrollBlacklist.json";

        private void LoadAutoEnrollBlacklist()
        {
            try
            {
                if (File.Exists(AutoEnrollBlacklistFile))
                {
                    autoEnrollBlacklist = JsonConvert.DeserializeObject<HashSet<ulong>>(File.ReadAllText(AutoEnrollBlacklistFile)) ?? new HashSet<ulong>();
                    Puts($"[Debug] Loaded {autoEnrollBlacklist.Count} auto-enroll blacklist entries.");
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to load auto enroll blacklist: {ex.Message}");
                autoEnrollBlacklist = new HashSet<ulong>();
            }
        }

        private void SaveAutoEnrollBlacklist()
        {
            try
            {
                File.WriteAllText(AutoEnrollBlacklistFile, JsonConvert.SerializeObject(autoEnrollBlacklist, Formatting.Indented));
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to save auto enroll blacklist: {ex.Message}");
            }
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

            Server.Broadcast(template, ulong.Parse(Configuration.ChatIconSteamId));

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

        private void SendFormattedMessage(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message)) return;

            try
            {
                // Format the message
                var formattedMessage = Configuration.ChatFormat.Replace("{message}", message);

                // Use a manual message format with SteamID
                player.SendConsoleCommand("chat.add", ulong.Parse(Configuration.ChatIconSteamId), Configuration.ChatUsername, formattedMessage);
            }
            catch (Exception ex)
            {
                PrintError($"[SendFormattedMessage] Error sending formatted message to player: {ex.Message}");
            }
        }

        private void SendPlayerMessage(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message))
            {
                PrintWarning("[SendPlayerMessage] Player is null or message is empty.");
                return;
            }

            try
            {
                // Format the message
                string formattedMessage = Configuration.ChatFormat.Replace("{message}", message);

                // Send the message using the proper command
                //player.SendConsoleCommand("chat.add", ulong.Parse(Configuration.ChatIconSteamId), Configuration.ChatUsername, formattedMessage);
                Server.Broadcast(formattedMessage, ulong.Parse(Configuration.ChatIconSteamId));
            }
            catch (Exception ex)
            {
                PrintError($"[SendPlayerMessage] Error sending message to player: {ex.Message}");
            }
        }

        private void SendTournamentMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                PrintWarning("[SendTournamentMessage] Attempted to send an empty or null message.");
                return;
            }

            Puts($"[Debug] Sending tournament message: {message}");

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                {
                    PrintWarning($"[SendTournamentMessage] Skipping player: {player?.displayName ?? "Unknown"} (Disconnected or null)");
                    continue;
                }

                try
                {
                    // Format the message
                    string formattedMessage = Configuration.ChatFormat.Replace("{message}", message);

                    // Send the message using the proper command
                    //player.SendConsoleCommand("chat.add", ulong.Parse(Configuration.ChatIconSteamId), Configuration.ChatUsername, formattedMessage);
                    Server.Broadcast(formattedMessage, ulong.Parse(Configuration.ChatIconSteamId));
                }
                catch (Exception ex)
                {
                    PrintError($"[SendTournamentMessage] Error sending message to player {player.displayName}: {ex.Message}");
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
                // Add player to auto-enroll blacklist so they are not auto-enrolled on rejoin
                autoEnrollBlacklist.Add(player.userID);
                SaveAutoEnrollBlacklist();

                // Save updated participants data
                SaveParticipantsData();

                // Log successful removal
                Puts($"[Debug] {player.displayName} ({player.userID}) removed from participantsData and added to auto-enroll blacklist.");

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
            if (isTournamentRunning)
            {
                // Display the time remaining in the tournament
                string timeRemainingMessage = GetTimeRemainingMessage();

                // Get total participants and top scorer
                var participants = participantsData.Values.OrderByDescending(p => p.Score).ToList();

                string topScorerMessage = participants.Any()
                    ? $"{participants.First().Name} with {participants.First().Score} points"
                    : "No participants yet.";

                string totalParticipants = participants.Count.ToString();

                // Prepare the response message
                var message = $"{timeRemainingMessage}\n" +
                            $"Total participants at this time: {totalParticipants}\n" +
                            $"Current top scorer: {topScorerMessage}";

                SendPlayerMessage(player, message);
            }
            else
            {
                // Calculate time remaining to the next tournament start
                string timeRemainingToStart = tournamentStartTime > DateTime.UtcNow
                    ? FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow)
                    : "N/A";

                // Notify the player about the next tournament start
                var message = "The tournament has not started yet.\n" +
                            $"Next tournament starts in: {timeRemainingToStart}";

                SendPlayerMessage(player, message);
            }
        }

        [ChatCommand("score_tournament")]
        private void ScoreTournamentCommand(BasePlayer player, string command, string[] args)

        {
            Puts($"[Debug] Player {player.displayName} ({player.UserIDString}) issued the /score_tournament command.");

            try
            {
                // Display scores based on current or last tournament data
                DisplayScores(player);
            }
            catch (Exception ex)
            {
                PrintError($"[Error] Failed to display scores: {ex.Message}");
                SendPlayerMessage(player, "An error occurred while fetching scores. Please try again later.");
            }
        }

        private void DisplayScores(BasePlayer player)
        {
            // Check if there are any participants
            if (!participantsData.Any())
            {
                Puts("[Debug] No participants found to display scores.");
                SendPlayerMessage(player, "No scores are available at the moment. Join the tournament to compete!");
                return;
            }

            // Sort participants by score in descending order
            var sortedParticipants = participantsData.Values
                .OrderByDescending(p => p.Score)
                .ToList();

            // Format the participant list for display
            var playerList = string.Join("\n", sortedParticipants.Select((p, i) => $"{i + 1}. {p.Name} ({p.Score} Points)"));

            // Notify the requesting player with the scores
            SendPlayerMessage(player, $"Current tournament scores:\n{playerList}");

            // Log the scores for debugging
            Puts($"[Debug] Displayed scores for player {player.displayName}: {playerList}");
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
                { "status_tournament", "View general stats." },
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
