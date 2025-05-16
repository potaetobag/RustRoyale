using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Linq;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using Rust;

namespace Oxide.Plugins
{
    [Info("RustRoyale", "Potaetobag", "1.2.2"), Description("Rust Royale custom tournament game mode with point-based scoring system.")]
    class RustRoyale : RustPlugin
    {
		private const string ConfigUiPanelName = "RustRoyale_Config_UI";
        private readonly Dictionary<string, string> pendingConfig = new Dictionary<string, string>();
		private HashSet<ulong> welcomeOptOut = new HashSet<ulong>();
		private string WelcomeOptOutFile => $"{DataDirectory}/WelcomeOptOut.json";
		private Dictionary<ulong, string> teamLeaderNames = new();
		private string TeamLeadersFile => $"{DataDirectory}/TeamLeaders.json";
    
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
			public bool PenaltyOnExitEnabled { get; set; } = true;
			public bool ShowWelcomeUI { get; set; } = true;
			public int PenaltyPointsOnExit { get; set; } = 25;
            public int StartHour { get; set; } = 12;
            public int StartMinute { get; set; } = 0;
            public int DurationHours { get; set; } = 125;
            public int DataRetentionDays { get; set; } = 30;
            public int TopPlayersToTrack { get; set; } = 3; // Default to Top 3 players
			public int TopClansToTrack { get; set; } = 3;
			public int JoinCutoffHours { get; set; } = 0; // 0 = No late‑join cut‑off
            public List<int> NotificationIntervals { get; set; } = new List<int> { 600, 60 }; // Default: every 10 minutes (600 seconds) and last minute (60 seconds)
            public Dictionary<string, int> ScoreRules { get; set; } = new Dictionary<string, int>
            {
                {"KILL", 5},     // Human player kills another human player
                {"DEAD", -3},    // Human player is killed by another human player
                {"JOKE", -1},    // Death by traps, self-inflicted damage
                {"NPC", 1},      // Kill an NPC (Murderer, Zombie, Scientist, Scarecrow)
                {"ENT", 20},     // Kill a Helicopter or Bradley Tank
                {"BRUH", -2},    // Death by an NPC, Helicopter, or Bradley
                {"WHY", 5}       // Award points for killing an animal (wolf, boar, bear, stag, deer) from over Xm away
            };
            public float AnimalKillDistance { get; set; } = 150f;
            public Dictionary<string, string> MessageTemplates { get; set; } = new Dictionary<string, string>
            {
                {"StartTournament", "Brace yourselves, champions! The tournament has begun! Time to show off those pro skills (or hilarious fails). Time left: {TimeRemaining}. Duration: {Duration} hours."},
                {"EndTournament", "The tournament is over! Congrats to the winners, and for the rest... better luck next time (maybe practice a bit?)."},
				{"ResumeTournament", "Welcome back, warriors! The tournament has been resumed. Time left: {TimeRemaining}. Duration: {Duration} hours."},
                {"PlayerScoreUpdate", "{PlayerName} just bagged {Score} point{PluralS} for {Action}. Somebody's on fire!"},
                {"TopPlayers", "Leaderboard time! Top {Count} players are: {PlayerList}. Did your name make the cut, or are you just here for fun?"},
                {"TimeRemaining", "Tick-tock! Time remaining in the tournament: {Time}. Don't waste it—score some points!"},
                {"JoinTournament", "{PlayerName} has entered the fray! Grab the popcorn, this should be good."},
                {"LeaveTournament", "{PlayerName} has exited the battlefield. Maybe they got scared? We’ll never know."},
                {"KitPurchaseSuccess", "{PlayerName} has successfully purchased the {KitName} kit for {Price} points. Your new balance is {TotalPoints} points."},
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
                {"TournamentCountdown", "Tournament starting soon! {Time} left to join."},
				{"LeaveTournamentPenalty", "{PlayerName} left the tournament and lost {PenaltyPoints} points!"}
            };
            public Dictionary<string, int> KitPrices { get; set; } = new Dictionary<string, int>
            {
                {"Starter", 5},
                {"Bronze", 25},
                {"Silver", 50},
                {"Gold", 75},
                {"Platinum", 100}
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

            if (!Enum.TryParse(Configuration.StartDay, true, out DayOfWeek _))
            {
                PrintWarning("Invalid StartDay in configuration. Defaulting to Friday.");
                Configuration.StartDay = "Friday";
                updated = true;
            }
			
			if (Configuration.PenaltyPointsOnExit < 0)
			{
				PrintWarning("Invalid PenaltyPointsOnExit. Defaulting to 25.");
				Configuration.PenaltyPointsOnExit = 25;
				updated = true;
			}

			if (Configuration.AnimalKillDistance <= 0)
			{
				PrintWarning("Invalid AnimalKillDistance. Defaulting to 150.");
				Configuration.AnimalKillDistance = 150f;
				updated = true;
			}

			if (Configuration.ScoreRules == null || Configuration.ScoreRules.Count == 0)
			{
				PrintWarning("ScoreRules missing or empty. Using defaults.");
				Configuration.ScoreRules = new Dictionary<string, int>
				{
					{"KILL", 5}, {"DEAD", -3}, {"JOKE", -1}, {"NPC", 1},
					{"ENT", 20}, {"BRUH", -2}, {"WHY", 5}
				};
				updated = true;
			}

			if (Configuration.KitPrices == null || Configuration.KitPrices.Count == 0)
			{
				PrintWarning("KitPrices missing or empty. Using defaults.");
				Configuration.KitPrices = new Dictionary<string, int>
				{
					{"Starter", 5}, {"Bronze", 25}, {"Silver", 50}, {"Gold", 75}, {"Platinum", 100}
				};
				updated = true;
			}

            if (Configuration.StartHour < 0 || Configuration.StartHour > 23)
            {
                PrintWarning("Invalid StartHour in configuration. Defaulting to 14 (2 PM).");
                Configuration.StartHour = 14;
                updated = true;
            }

            if (Configuration.StartMinute < 0 || Configuration.StartMinute > 59)
            {
                PrintWarning("Invalid StartMinute in configuration. Defaulting to 0 (top of the hour).");
                Configuration.StartMinute = 0;
                updated = true;
            }

            if (Configuration.DurationHours < 0.0167 || Configuration.DurationHours > 872) // Limit to 36 days
            {
                PrintWarning($"Invalid DurationHours in configuration: {Configuration.DurationHours}. Defaulting to 72 hours.");
                Configuration.DurationHours = 72;
                updated = true;
            }
            else if (Configuration.DurationHours < 1 && Configuration.DurationHours >= 0.0167)
            {
                Puts($"[Debug] Short tournament duration detected: {Configuration.DurationHours} hours. This is valid.");
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

            if (Configuration.NotificationIntervals == null || !Configuration.NotificationIntervals.Any())
            {
                PrintWarning("NotificationIntervals is invalid or missing. Defaulting to every 10 minutes and the last minute.");
                Configuration.NotificationIntervals = new List<int> { 600, 60 };
                updated = true;
            }

            if (Configuration.NotificationIntervals.Any(interval => interval <= 0))
            {
                PrintWarning("NotificationIntervals contains invalid values. Removing non-positive intervals.");
                Configuration.NotificationIntervals = Configuration.NotificationIntervals.Where(interval => interval > 0).ToList();
                updated = true;
            }
			
			if (Configuration.ShowWelcomeUI != true && Configuration.ShowWelcomeUI != false)
			{
				PrintWarning("Invalid 'ShowWelcomeUI'. Defaulting to true.");
				Configuration.ShowWelcomeUI = true;
				updated = true;
			}
			
			if (Configuration.TopClansToTrack <= 0)
			{
				PrintWarning("Invalid TopClansToTrack in configuration. Defaulting to 3.");
				Configuration.TopClansToTrack = 3;
				updated = true;
			}
			
			if (Configuration.JoinCutoffHours < 0 ||
				Configuration.JoinCutoffHours > Configuration.DurationHours)
			{
				PrintWarning($"JoinCutoffHours was {Configuration.JoinCutoffHours}; " +
							 $"must be between 0 and DurationHours ({Configuration.DurationHours}). " +
							 $"Defaulting to 6.");
				Configuration.JoinCutoffHours = 6;
				updated = true;
			}

            if (updated)
            {
                SaveConfig();
                Puts("Configuration updated with validated defaults.");
            }

            Puts($"Configuration validated: StartDay={Configuration.StartDay}, StartHour={Configuration.StartHour}, DurationHours={Configuration.DurationHours}, DataRetentionDays={Configuration.DataRetentionDays}, TopPlayersToTrack={Configuration.TopPlayersToTrack}, TopClansToTrack={Configuration.TopClansToTrack}, NotificationIntervals={string.Join(", ", Configuration.NotificationIntervals)}.");

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
	
	#region UI
	
		private void LoadWelcomeOptOut()
		{
			if (File.Exists(WelcomeOptOutFile))
			{
				welcomeOptOut = JsonConvert.DeserializeObject<HashSet<ulong>>(File.ReadAllText(WelcomeOptOutFile)) ?? new HashSet<ulong>();
			}
		}

		private void SaveWelcomeOptOut()
		{
			File.WriteAllText(WelcomeOptOutFile, JsonConvert.SerializeObject(welcomeOptOut, Formatting.Indented));
		}
		
		private const string WelcomeUiPanelName = "RustRoyale_Welcome_UI";

		private void ShowWelcomeUI(BasePlayer player)
		{
			CuiHelper.DestroyUi(player, WelcomeUiPanelName);
			var container = new CuiElementContainer();

			container.Add(new CuiPanel
			{
				Image = { Color = "0 0 0 0.6" },
				RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
				CursorEnabled = true
			}, "Overlay", WelcomeUiPanelName);

			container.Add(new CuiPanel
			{
				Image = { Color = "0.1 0.1 0.1 0.95" },
				RectTransform = { AnchorMin = "0.15 0.1", AnchorMax = "0.85 0.9" }
			}, WelcomeUiPanelName, $"{WelcomeUiPanelName}.main");

			string scrollView = $"{WelcomeUiPanelName}.scroll";
			container.Add(new CuiElement
			{
				Name = scrollView,
				Parent = $"{WelcomeUiPanelName}.main",
				Components =
				{
					new CuiScrollViewComponent(),
					new CuiRectTransformComponent { AnchorMin = "0.05 0.1", AnchorMax = "0.95 0.95" }
				}
			});

			float y = 0.95f;

			void AddTextBlock(string text, int size = 14)
			{
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = $"0 {y - 0.18f}", AnchorMax = $"1 {y}" },
					Text = { Text = text, FontSize = size, Align = TextAnchor.UpperLeft }
				}, scrollView);
				y -= 0.06f;
			}

			container.Add(new CuiElement
			{
				Name = $"{WelcomeUiPanelName}.logo",
				Parent = scrollView,
				Components =
				{
					new CuiRawImageComponent { Url = "https://panels.twitch.tv/panel-1025512005-image-9ae6176b-7901-47e0-a0a2-2c16fed78df3" },
					new CuiRectTransformComponent { AnchorMin = "0.4 0.91", AnchorMax = "0.6 0.98" }
				}
			});
			
			y -= 0.07f;

			AddTextBlock("<b>Welcome to RustRoyale!</b>", 18);
			AddTextBlock("Get ready for an adrenaline-pumping Rust experience like no other. RustRoyale is a competitive tournament that rewards skill, strategy, and survival instincts. Earn points by taking down other players, eliminating NPC threats, and showing off your long-range hunting skills. But be warned—deaths, especially from traps or careless encounters with NPCs, will cost you. Sharpen your weapons, team up or go solo, and claim your spot at the top of the leaderboard!");
			y -= 0.1f;
			AddTextBlock("Join our Discord channel for updates: https://discord.gg/y2v4RaKP");

			y -= 0.1f;

			float baseY = y;
			float colRowHeight = 0.035f;
			float colRowGap = 0.005f;
			float colRowSize = colRowHeight + colRowGap;
			int maxRows = Math.Max(Math.Max(3, Configuration.ScoreRules.Count), Configuration.KitPrices.Count);

			float col1Min = 0f, col1Max = 0.32f;
			float col2Min = 0.34f, col2Max = 0.66f;
			float col3Min = 0.68f, col3Max = 1f;

			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = $"{col1Min} {baseY}", AnchorMax = $"{col1Max} {baseY + colRowHeight}" },
				Text = { Text = "<b>Tournament Stats:</b>", FontSize = 14, Align = TextAnchor.MiddleLeft }
			}, scrollView);

			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = $"{col2Min} {baseY}", AnchorMax = $"{col2Max} {baseY + colRowHeight}" },
				Text = { Text = "<b>Scoring Rules:</b>", FontSize = 14, Align = TextAnchor.MiddleLeft }
			}, scrollView);

			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = $"{col3Min} {baseY}", AnchorMax = $"{col3Max} {baseY + colRowHeight}" },
				Text = { Text = "<b>Kit Prices:</b>", FontSize = 14, Align = TextAnchor.MiddleLeft }
			}, scrollView);

			y = baseY - colRowSize;

			for (int i = 0; i < maxRows; i++)
			{
				if (i == 0)
				{
					container.Add(new CuiLabel
					{
						RectTransform = { AnchorMin = $"{col1Min} {y - colRowHeight}", AnchorMax = $"{col1Max} {y}" },
						Text = { Text = $"• Duration: {Configuration.DurationHours} hours", FontSize = 13, Align = TextAnchor.MiddleLeft }
					}, scrollView);
				}
				else if (i == 1)
				{
					string timeLeft = "N/A";
					if (tournamentStartTime > DateTime.MinValue)
					{
						var endTime = tournamentStartTime.AddHours(Configuration.DurationHours);
						var remaining = endTime - DateTime.UtcNow;
						timeLeft = remaining.TotalSeconds > 0
							? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
							: "Tournament ended";
					}

					container.Add(new CuiLabel
					{
						RectTransform = { AnchorMin = $"{col1Min} {y - colRowHeight}", AnchorMax = $"{col1Max} {y}" },
						Text = { Text = $"• Time Left: {timeLeft}", FontSize = 13, Align = TextAnchor.MiddleLeft }
					}, scrollView);
				}
				else if (i == 2)
				{
					container.Add(new CuiLabel
					{
						RectTransform = { AnchorMin = $"{col1Min} {y - colRowHeight}", AnchorMax = $"{col1Max} {y}" },
						Text = { Text = $"• Participants: {participants.Count}", FontSize = 13, Align = TextAnchor.MiddleLeft }
					}, scrollView);
				}

				if (i < Configuration.ScoreRules.Count)
				{
					var rule = Configuration.ScoreRules.ElementAt(i);
					string friendly = rule.Key switch
					{
						"KILL" => "Kill another player",
						"DEAD" => "Killed by a player",
						"JOKE" => "Trap/fall death",
						"NPC" => "Kill an NPC",
						"ENT" => "Kill Heli/Bradley",
						"BRUH" => "Killed by Heli/NPC",
						"WHY" => $"Animal kill >{Configuration.AnimalKillDistance}m",
						_ => rule.Key
					};

					container.Add(new CuiLabel
					{
						RectTransform = { AnchorMin = $"{col2Min} {y - colRowHeight}", AnchorMax = $"{col2Max} {y}" },
						Text = { Text = $"• {friendly}: {rule.Value} pts", FontSize = 13, Align = TextAnchor.MiddleLeft }
					}, scrollView);
				}

				if (i < Configuration.KitPrices.Count)
				{
					var kit = Configuration.KitPrices.ElementAt(i);
					container.Add(new CuiLabel
					{
						RectTransform = { AnchorMin = $"{col3Min} {y - colRowHeight}", AnchorMax = $"{col3Max} {y}" },
						Text = { Text = $"• {kit.Key} Kit: {kit.Value} pts", FontSize = 13, Align = TextAnchor.MiddleLeft }
					}, scrollView);
				}

				y -= colRowSize;
			}

			string toggleLabel = welcomeOptOut.Contains(player.userID) ? "Show next time" : "Don’t show again";
			string toggleCommand = $"welcomeui_toggle {player.userID}";
			container.Add(new CuiButton
			{
				Button = { Command = toggleCommand, Color = "0.2 0.5 0.8 1" },
				RectTransform = { AnchorMin = "0.35 0.01", AnchorMax = "0.49 0.06" },
				Text = { Text = toggleLabel, FontSize = 12, Align = TextAnchor.MiddleCenter }
			}, $"{WelcomeUiPanelName}.main");

			container.Add(new CuiButton
			{
				Button = { Color = "0.7 0.2 0.2 1", Command = "welcomeui_close" },
				RectTransform = { AnchorMin = "0.51 0.01", AnchorMax = "0.65 0.06" },
				Text = { Text = "Close", FontSize = 14, Align = TextAnchor.MiddleCenter }
			}, $"{WelcomeUiPanelName}.main");
			
			y -= 0.07f;

			AddTextBlock("You can use commands such as /help_tournament to learn more.");
			

			CuiHelper.AddUi(player, container);
		}
		
		[ConsoleCommand("welcomeui_close")]
		private void CloseWelcomeUI(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player != null)
			{
				CuiHelper.DestroyUi(player, WelcomeUiPanelName);
			}
		}

		[ConsoleCommand("welcomeui_toggle")]
		private void ToggleWelcomeUI(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null || arg.Args.Length < 1) return;

			ulong id = player.userID;
			if (welcomeOptOut.Contains(id))
			{
				welcomeOptOut.Remove(id);
			}
			else
			{
				welcomeOptOut.Add(id);
			}

			SaveWelcomeOptOut();
			ShowWelcomeUI(player);
		}
		
		private void ShowConfigUI(BasePlayer player)
		{
			CuiHelper.DestroyUi(player, ConfigUiPanelName);
			var container = new CuiElementContainer();

			container.Add(new CuiPanel
			{
				Image = { Color = "0 0 0 0.8" },
				RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
				CursorEnabled = true
			}, "Overlay", ConfigUiPanelName);

			container.Add(new CuiPanel
			{
				Image = { Color = "0.1 0.1 0.1 0.95" },
				RectTransform = { AnchorMin = "0.2 0.1", AnchorMax = "0.8 0.9" }
			}, ConfigUiPanelName, $"{ConfigUiPanelName}.main");

			container.Add(new CuiPanel
			{
				Image = { Color = "0.25 0.35 0.45 1" },
				RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 1" }
			}, $"{ConfigUiPanelName}.main", $"{ConfigUiPanelName}.header");

			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.9 1" },
				Text = { Text = "⚙ RustRoyale Configuration", FontSize = 18, Align = TextAnchor.MiddleLeft }
			}, $"{ConfigUiPanelName}.header");

			container.Add(new CuiButton
			{
				Button = { Color = "0.8 0.2 0.2 1", Command = "config_cancel" },
				RectTransform = { AnchorMin = "0.93 0.1", AnchorMax = "0.98 0.9" },
				Text = { Text = "<b>X</b>", FontSize = 16, Align = TextAnchor.MiddleCenter }
			}, $"{ConfigUiPanelName}.header");

			float y = 0.87f;
			float xLeftLabel = 0.05f, xLeftInput = 0.25f;
			float xRightLabel = 0.55f, xRightInput = 0.75f;

			void AddInput(string label, string key, string value)
			{
				float xLabel = xLeftLabel;
				float xInput = xLeftInput + 0.02f;

				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = $"{xLabel} {y}", AnchorMax = $"{xLabel + 0.2f} {y + 0.035f}" },
					Text = { Text = label, FontSize = 14, Align = TextAnchor.MiddleLeft }
				}, $"{ConfigUiPanelName}.main");

				container.Add(new CuiPanel
				{
					Image = { Color = "0.8 0.8 0.8 0.3" },
					RectTransform = { AnchorMin = $"{xInput} {y}", AnchorMax = $"{xInput + 0.2f} {y + 0.035f}" }
				}, $"{ConfigUiPanelName}.main");

				container.Add(new CuiElement
				{
					Name = $"{key}Input",
					Parent = $"{ConfigUiPanelName}.main",
					Components =
					{
						new CuiRectTransformComponent { AnchorMin = $"{xInput} {y}", AnchorMax = $"{xInput + 0.2f} {y + 0.035f}" },
						new CuiInputFieldComponent
						{
							Text = value,
							Align = TextAnchor.MiddleLeft,
							Command = $"config_set {key}"
						}
					}
				});

				y -= 0.05f;
			}

			void AddToggle(string label, string key, bool value)
			{
				float xLabel = xLeftLabel;
				float xButton = xLeftInput + 0.02f;

				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = $"{xLabel} {y}", AnchorMax = $"{xLabel + 0.2f} {y + 0.035f}" },
					Text = { Text = label, FontSize = 14, Align = TextAnchor.MiddleLeft }
				}, $"{ConfigUiPanelName}.main");

				container.Add(new CuiPanel
				{
					Image = { Color = "0.8 0.8 0.8 0.3" },
					RectTransform = { AnchorMin = $"{xButton} {y}", AnchorMax = $"{xButton + 0.2f} {y + 0.035f}" }
				}, $"{ConfigUiPanelName}.main");

				container.Add(new CuiButton
				{
					Button = { Command = $"config_set {key} {!value}", Color = value ? "0.3 0.6 0.3 1" : "0.6 0.3 0.3 1" },
					RectTransform = { AnchorMin = $"{xButton} {y}", AnchorMax = $"{xButton + 0.2f} {y + 0.035f}" },
					Text = { Text = value.ToString(), FontSize = 14, Align = TextAnchor.MiddleCenter }
				}, $"{ConfigUiPanelName}.main");

				y -= 0.05f;
			}

			AddInput("Timezone", "Timezone", Configuration.Timezone);
			AddInput("StartDay", "StartDay", Configuration.StartDay);
			AddInput("StartHour", "StartHour", Configuration.StartHour.ToString());
			AddInput("StartMinute", "StartMinute", Configuration.StartMinute.ToString());
			AddInput("DurationHours", "DurationHours", Configuration.DurationHours.ToString());
			AddInput("TopPlayersToTrack", "TopPlayersToTrack", Configuration.TopPlayersToTrack.ToString());
			AddInput("JoinCutoffHours", "JoinCutoffHours", Configuration.JoinCutoffHours.ToString());
			AddInput("AnimalKillDistance", "AnimalKillDistance", Configuration.AnimalKillDistance.ToString());

			AddToggle("Show Welcome Message", "ShowWelcomeUI", Configuration.ShowWelcomeUI);
			AddToggle("AutoStartEnabled", "AutoStartEnabled", Configuration.AutoStartEnabled);
			AddToggle("AutoEnrollEnabled", "AutoEnrollEnabled", Configuration.AutoEnrollEnabled);
			AddToggle("PenaltyOnExitEnabled", "PenaltyOnExitEnabled", Configuration.PenaltyOnExitEnabled);

			AddInput("PenaltyPointsOnExit", "PenaltyPointsOnExit", Configuration.PenaltyPointsOnExit.ToString());

			// ScoreRules + KitPrices
			float groupY = 0.87f;

			foreach (var kv in Configuration.ScoreRules)
			{
				float marginX = xRightInput + 0.03f;

				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = $"{xRightLabel} {groupY}", AnchorMax = $"{xRightLabel + 0.2f} {groupY + 0.035f}" },
					Text = { Text = $"ScoreRules[{kv.Key}]", FontSize = 14, Align = TextAnchor.MiddleLeft }
				}, $"{ConfigUiPanelName}.main");

				container.Add(new CuiPanel
				{
					Image = { Color = "0.8 0.8 0.8 0.3" },
					RectTransform = { AnchorMin = $"{marginX} {groupY}", AnchorMax = $"{marginX + 0.15f} {groupY + 0.035f}" }
				}, $"{ConfigUiPanelName}.main");

				container.Add(new CuiElement
				{
					Name = $"ScoreRule_{kv.Key}",
					Parent = $"{ConfigUiPanelName}.main",
					Components = {
						new CuiRectTransformComponent { AnchorMin = $"{marginX} {groupY}", AnchorMax = $"{marginX + 0.15f} {groupY + 0.035f}" },
						new CuiInputFieldComponent {
							Text = kv.Value.ToString(),
							Align = TextAnchor.MiddleLeft,
							Command = $"config_set ScoreRules.{kv.Key}"
						}
					}
				});

				groupY -= 0.05f;
			}

			groupY -= 0.01f;

			foreach (var kv in Configuration.KitPrices)
			{
				float marginX = xRightInput + 0.03f;

				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = $"{xRightLabel} {groupY}", AnchorMax = $"{xRightLabel + 0.2f} {groupY + 0.035f}" },
					Text = { Text = $"KitPrices[{kv.Key}]", FontSize = 14, Align = TextAnchor.MiddleLeft }
				}, $"{ConfigUiPanelName}.main");

				container.Add(new CuiPanel
				{
					Image = { Color = "0.8 0.8 0.8 0.3" },
					RectTransform = { AnchorMin = $"{marginX} {groupY}", AnchorMax = $"{marginX + 0.15f} {groupY + 0.035f}" }
				}, $"{ConfigUiPanelName}.main");

				container.Add(new CuiElement
				{
					Name = $"KitPrice_{kv.Key}",
					Parent = $"{ConfigUiPanelName}.main",
					Components = {
						new CuiRectTransformComponent { AnchorMin = $"{marginX} {groupY}", AnchorMax = $"{marginX + 0.15f} {groupY + 0.035f}" },
						new CuiInputFieldComponent {
							Text = kv.Value.ToString(),
							Align = TextAnchor.MiddleLeft,
							Command = $"config_set KitPrices.{kv.Key}"
						}
					}
				});

				groupY -= 0.05f;
			}

			// Save & Cancel
			container.Add(new CuiButton
			{
				Button = { Command = "config_save", Color = "0.2 0.6 0.2 1" },
				RectTransform = { AnchorMin = "0.27 0.02", AnchorMax = "0.47 0.07" },
				Text = { Text = "Save & Reload", FontSize = 14, Align = TextAnchor.MiddleCenter }
			}, $"{ConfigUiPanelName}.main");

			container.Add(new CuiButton
			{
				Button = { Command = "config_cancel", Color = "0.6 0.2 0.2 1" },
				RectTransform = { AnchorMin = "0.53 0.02", AnchorMax = "0.73 0.07" },
				Text = { Text = "Cancel", FontSize = 14, Align = TextAnchor.MiddleCenter }
			}, $"{ConfigUiPanelName}.main");

			CuiHelper.AddUi(player, container);
		}
		
		private void CloseConfigUI(BasePlayer player)
		{
			CuiHelper.DestroyUi(player, ConfigUiPanelName);
		}
		
		[ConsoleCommand("config_set")]
		private void ConsoleConfigSet(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null || !HasAdminPermission(player)) return;
			var args = arg.Args;
			if (args.Length < 2) return;

			string key = args[0];
			string val = string.Join(" ", args.Skip(1));
			pendingConfig[key] = val;
			player.ChatMessage($"<color=#ffd479>RustRoyale:</color> Set {key} → {val}");
		}
		
		[ConsoleCommand("config_save")]
		private void ConfigSave(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null || !HasAdminPermission(player)) return;

			foreach (var kv in pendingConfig)
			{
				var prop = typeof(ConfigData).GetProperty(kv.Key);
				if (prop == null) { player.ChatMessage($"Unknown property: {kv.Key}"); continue; }

				try
				{
					object converted = Convert.ChangeType(kv.Value, prop.PropertyType);
					prop.SetValue(Configuration, converted);
				}
				catch
				{
					player.ChatMessage($"Failed to parse {kv.Key} as {prop.PropertyType.Name}");
				}
			}

			pendingConfig.Clear();
			SaveConfig();
			ApplyConfiguration();

			CloseConfigUI(player);
		}
		
		[ConsoleCommand("config_cancel")]
		private void ConfigCancel(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null || !HasAdminPermission(player)) return;
			pendingConfig.Clear();
			CloseConfigUI(player);
		}
		
	#endregion

    #region Permissions
        private const string AdminPermission = "rustroyale.admin";

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
			LoadWelcomeOptOut();
            ValidateConfiguration();
			NormaliseKitPrices();
            LoadAutoEnrollBlacklist();
            LoadParticipantsData();
			LoadTeamLeaderNames();
			ResumeInterruptedTournament();
			if (!isTournamentRunning)
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
        private string currentTournamentFile;

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
					PrintWarning("Tournament file has not been initialized. Calling StartTournamentFile()...");
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
	
	#region On Start
		private void OnServerInitialized()
		{
			foreach (var player in BasePlayer.activePlayerList)
			{
				OnPlayerInit(player);
			}
		}
	#endregion

    #region Schedule Tournament
        private readonly Dictionary<ulong, PlayerStats> playerStats = new Dictionary<ulong, PlayerStats>();
        private readonly HashSet<ulong> participants = new HashSet<ulong>();
        private readonly HashSet<ulong> openTournamentPlayers = new HashSet<ulong>();
		private readonly HashSet<ulong> inactiveParticipants = new HashSet<ulong>();
		private Dictionary<ulong, (BasePlayer Attacker, float TimeStamp)> lastDamageRecords 
			= new Dictionary<ulong, (BasePlayer Attacker, float TimeStamp)>();
        private bool isTournamentRunning = false;
        private DateTime tournamentStartTime;
        private DateTime tournamentEndTime;
        private Timer countdownTimer;

        private bool IsWithinCutoffPeriod()
		{
			if (Configuration.JoinCutoffHours == 0) return true;

			return isTournamentRunning &&
				   DateTime.UtcNow < tournamentStartTime.AddHours(Configuration.JoinCutoffHours);
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

			if (timeUntilTarget.TotalSeconds <= 1.0)
			{
				Puts($"[Debug] Timer expired immediately for target: {targetTime}. Invoking completion.");
				onCompletion?.Invoke();
				return;
			}

			Puts($"[Debug] Starting open-ended countdown timer for about {timeUntilTarget.TotalSeconds:F1} seconds.");
			countdownTimer?.Destroy();

			countdownTimer = timer.Every(1f, () =>
			{
				TimeSpan remainingTime = targetTime - DateTime.UtcNow;
				
				NotifyTournamentCountdown(remainingTime);

				if (remainingTime.TotalSeconds <= 1.0)
				{
					Puts("[Debug] Countdown timer expired; invoking completion.");
					countdownTimer?.Destroy();
					countdownTimer = null;

					onCompletion?.Invoke();
				}
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
				Puts($"[Debug] The server thinks it's currently {now:yyyy-MM-dd HH:mm:ss} UTC.");
                DateTime localNow = GetLocalTime(now);
				Puts($"[Debug] localNow is {localNow:yyyy-MM-dd HH:mm:ss} (Timezone={Configuration.Timezone}).");
				
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
				
				int activeCount = participantsData.Keys.Where(id => !inactiveParticipants.Contains(id)).Count();
				string startTimeLocal = GetLocalTime(tournamentStartTime).ToString("yyyy-MM-dd HH:mm:ss");
				string scheduleMessage = $"Heads up, warriors! The tournament kicks off at {startTimeLocal} ({Configuration.Timezone}) and will rage for {Configuration.DurationHours} hours. Already, {activeCount} heroes have signed up to battle it out. If you’re not in yet, type /open_tournament and join the fray!";
				SendTournamentMessage(scheduleMessage);
				if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
				{
					SendDiscordMessage(scheduleMessage);
				}

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

                string globalMessage = FormatMessage(Configuration.MessageTemplates["TournamentCountdown"], new Dictionary<string, string>
                {
                    { "Time", formattedTime }
                });
                SendTournamentMessage(globalMessage);

                Puts($"[Debug] Tournament countdown message sent to global chat: {globalMessage}");

                Notify("TournamentCountdown", null, placeholders: new Dictionary<string, string>
                {
                    { "Time", formattedTime }
                });

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
                // Puts($"[Debug] No notification sent for {remainingSeconds}s remaining. Last notified: {lastNotifiedMinutes}s");
            }
        }

        private void NotifyTournamentStartImminent()
        {
            string timeRemaining = FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow);

            Notify("TournamentAboutToStart", null, placeholders: new Dictionary<string, string>
            {
                { "Time", timeRemaining }
            });

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
	
		private void ResumeInterruptedTournament()
		{
			EnsureDataDirectory();

			string latestFile = null;
			foreach (var file in Directory.EnumerateFiles(DataDirectory, "Tournament_*.data"))
			{
				if (latestFile == null ||
					string.Compare(Path.GetFileName(file), Path.GetFileName(latestFile), StringComparison.Ordinal) > 0)
				{
					latestFile = file;
				}
			}
			if (latestFile == null) return;

			var lines = File.ReadAllLines(latestFile);
			if (!lines.Any(l => l.Contains("Tournament ended successfully.")))
			{
				Puts($"[Info] Resuming interrupted tournament from log: {Path.GetFileName(latestFile)}");
				currentTournamentFile = latestFile;

				var stamp = Path.GetFileNameWithoutExtension(latestFile).Split('_')[1];
				var startUtc = DateTime.ParseExact(
					stamp,
					"yyyyMMddHHmmss",
					CultureInfo.InvariantCulture,
					DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
				);

				tournamentStartTime = startUtc;
				tournamentEndTime   = startUtc.AddHours(Configuration.DurationHours);
				isTournamentRunning = true;

				if (DateTime.UtcNow >= tournamentEndTime)
				{
					EndTournament();
				}
				else
				{
					ScheduleTournamentEnd();

					TimeSpan remaining = tournamentEndTime - DateTime.UtcNow;
					var msg = FormatMessage(
						Configuration.MessageTemplates["ResumeTournament"],
						new Dictionary<string,string>
						{
							{ "TimeRemaining", FormatTimeRemaining(remaining) },
							{ "Duration",      Configuration.DurationHours.ToString() }
						}
					);

					SendTournamentMessage(msg);
					if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
						SendDiscordMessage(msg);
				}
			}
		}
	
        [ChatCommand("start_tournament")]
        private void StartTournamentCommand(BasePlayer player, string command, string[] args)
        {
            Puts($"[Debug] Player {player.displayName} ({player.UserIDString}) issued the /start_tournament command.");

            if (!ValidateAdminCommand(player, "start the tournament"))
            {
                Puts($"[Debug] Player {player.displayName} ({player.UserIDString}) lacks the required admin permissions.");
                return;
            }

            if (isTournamentRunning)
            {
                Puts("[Debug] Attempt to start tournament while one is already running.");

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

            Puts($"[Debug] Tournament started successfully by {player.displayName} ({player.UserIDString}).");
        }

        private void StartTournament()
		{
			Puts("[Debug] StartTournament invoked.");
			
			participants.Clear();
			foreach (var id in participantsData.Keys)
				participants.Add(id);
			Puts("[Debug] Participants HashSet rebuilt for new tournament.");

			foreach (var p in participantsData.Values)
				p.Score = 0;
			SaveParticipantsData();
			Puts("[Debug] All participant scores reset to 0 and saved.");

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
					PrintError("[Error] Invalid tournament duration. Ensure `DurationHours` > 0.");
					return;
				}

				isTournamentRunning = true;
				tournamentEndTime = DateTime.UtcNow.AddHours(Configuration.DurationHours);
				Puts($"[Info] Tournament started. Duration: {Configuration.DurationHours} hours. Ends at: {tournamentEndTime:yyyy-MM-dd HH:mm:ss} UTC.");

				playerStats.Clear();

				if (countdownTimer != null)
				{
					Puts("[Debug] Destroying existing countdown timer.");
					countdownTimer.Destroy();
					countdownTimer = null;
				}

				if (participantsData.Values.Any())
					LogEvent($"[Info] Participants at tournament start: {string.Join(", ", participantsData.Values.Select(p => p.Name))}");
				else
					LogEvent("[Info] No participants at tournament start.");

				LogEvent("[Info] Tournament started successfully.");

				ScheduleTournamentEnd();

				string globalMessage = FormatMessage(Configuration.MessageTemplates["StartTournament"], new Dictionary<string, string>
				{
					{ "TimeRemaining", FormatTimeRemaining(tournamentEndTime - DateTime.UtcNow) },
					{ "Duration", Configuration.DurationHours.ToString() }
				});
				SendTournamentMessage(globalMessage);

				if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
				{
					Puts("[Debug] Sending Discord notification for tournament start.");
					SendDiscordMessage(globalMessage);
				}
			}
			catch (Exception ex)
			{
				PrintError($"[Error] Failed to start tournament: {ex.Message}");
			}
		}
		
		private string GetGroupKey(ulong userId)
		{
			if (Clans != null)
			{
				var clanTag = Clans.Call("GetClanTag", userId) as string;
				if (!string.IsNullOrEmpty(clanTag))
					return clanTag;
			}

			var basePlayer = BasePlayer.FindByID(userId);
			if (basePlayer != null && basePlayer.currentTeam != 0)
			{
				ulong teamId = basePlayer.currentTeam;
				if (!teamLeaderNames.ContainsKey(teamId) &&
					RelationshipManager.ServerInstance.teams.TryGetValue(teamId, out var team) &&
					team.teamLeader == userId)
				{
					teamLeaderNames[teamId] = basePlayer.displayName;
					SaveTeamLeaderNames(); // ✅ Persist the new value
					Puts($"[Debug] Leader {basePlayer.displayName} cached for team {teamId} via GetGroupKey()");
				}

				return teamId.ToString();
			}

			var groups = permission.GetUserGroups(userId.ToString());
			return groups.Length > 0 ? groups[0] : null;
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

					isTournamentRunning = false;
					Puts("RustRoyale: Tournament ended!");

					var sortedParticipants = participantsData.Values
						.OrderByDescending(p => p.Score)
						.ToList();

					SaveTournamentHistory(sortedParticipants);
					LogEvent("Tournament ended successfully.");

					string resultsMessage = "Leaderboard:\n";
					if (sortedParticipants.Any())
					{
						resultsMessage += string.Join("\n", sortedParticipants
							.Select((p, idx) => $"{idx + 1}. {p.Name} - {p.Score} Points"));
					}
					else
					{
						resultsMessage += "No participants scored points in this tournament.";
					}

					var clanTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
					var groupNames = new Dictionary<string, string>();

					foreach (var ps in participantsData.Values)
					{
						string groupKey = GetGroupKey(ps.UserId);

						if (string.IsNullOrEmpty(groupKey) ||
							groupKey.Equals("default", StringComparison.OrdinalIgnoreCase) ||
							groupKey.Equals("No Group", StringComparison.OrdinalIgnoreCase))
							continue;

						string displayName = groupKey;

						if (ulong.TryParse(groupKey, out ulong teamId) &&
							RelationshipManager.ServerInstance.teams.ContainsKey(teamId))
						{
							if (teamLeaderNames.TryGetValue(teamId, out var cachedName))
								{
									displayName = $"{cachedName}'s team";
								}
								else
								{
									displayName = $"Team {teamId}";
								}
						}

						groupNames[groupKey] = displayName;

						if (!clanTotals.ContainsKey(groupKey))
							clanTotals[groupKey] = 0;

						clanTotals[groupKey] += ps.Score;
					}

					var topClans = clanTotals
						.OrderByDescending(kv => kv.Value)
						.Take(Configuration.TopClansToTrack)
						.ToList();

					var globalMessage = new StringBuilder();
					globalMessage.AppendLine(FormatMessage(Configuration.MessageTemplates["EndTournament"], new Dictionary<string, string>()));
					globalMessage.AppendLine();
					globalMessage.AppendLine(resultsMessage);

					if (topClans.Any())
					{
						globalMessage.AppendLine();
						globalMessage.AppendLine("Top Clans/Groups:");
						globalMessage.AppendLine(string.Join("\n", topClans
							.Select((kv, idx) =>
							{
								string name = groupNames.ContainsKey(kv.Key) ? groupNames[kv.Key] : kv.Key;
								return $"{idx + 1}. {name} ({kv.Value} pts)";
							})));
					}

					SendTournamentMessage(globalMessage.ToString().TrimEnd());
					if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
						SendDiscordMessage(globalMessage.ToString().TrimEnd());

					Puts($"[Debug] Notifications sent for tournament end:\n{globalMessage}");
					Puts($"[Debug] Tournament successfully ended. Total participants: {sortedParticipants.Count}");

					foreach (var p in participantsData.Values)
						p.Score = 0;
					SaveParticipantsData();
					Puts("[Debug] All participant scores reset to 0 and saved at tournament end.");

					countdownTimer?.Destroy();
					countdownTimer = null;
					Puts("[Debug] Scheduling the next tournament.");
					ScheduleTournament();
				}
				catch (Exception ex)
				{
					PrintError($"Failed to end tournament: {ex.Message}");
				}
			}

        private void OnPlayerInit(BasePlayer player)
		{
			if (player == null || string.IsNullOrEmpty(player.displayName))
				return;

			playerNameCache[player.userID] = player.displayName;

			if (player.currentTeam != 0 &&
				RelationshipManager.ServerInstance.teams.TryGetValue(player.currentTeam, out var team) &&
				team.teamLeader == player.userID)
			{
				if (!teamLeaderNames.ContainsKey(player.currentTeam))
				{
					teamLeaderNames[player.currentTeam] = player.displayName;
					SaveTeamLeaderNames(); // Optionally persist it right away
					Puts($"[Debug] Cached leader name '{player.displayName}' for team {player.currentTeam}");
				}
			}

			if (participantsData.TryGetValue(player.userID, out var participant) && participant.Name == "Unknown")
			{
				participant.Name = player.displayName;
				SaveParticipantsData();
				Puts($"Updated participant data: {player.userID} now has name '{player.displayName}'.");
			}

			if (Configuration.AutoEnrollEnabled && !autoEnrollBlacklist.Contains(player.userID) && !participantsData.ContainsKey(player.userID))
			{
				participantsData.TryAdd(player.userID, new PlayerStats(player.userID));

				if (isTournamentRunning)
				{
					participants.Add(player.userID);
					Puts($"[Debug] {player.displayName} was auto-enrolled and added to active tournament participants.");
				}

				SaveParticipantsData();
				SendPlayerMessage(player, "You have been automatically enrolled into the RustRoyale tournament system, you can opt out by entering /close_tournament.");
				Puts($"[Debug] Automatically enrolled player: {player.displayName} ({player.userID})");
			}
		}
		
		private void OnPlayerConnected(BasePlayer player)
		{
			if (player == null) return;

			timer.Once(2f, () =>
			{
				if (player != null && player.IsConnected && Configuration.ShowWelcomeUI && !welcomeOptOut.Contains(player.userID))
				{
					Puts($"[Debug] Showing welcome UI for {player.displayName} ({player.UserIDString})");
					ShowWelcomeUI(player);
				}
			});
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

		private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
		{
			if (entity is BasePlayer victim && info != null && info.InitiatorPlayer != null)
			{
				lastDamageRecords[victim.userID] = (info.InitiatorPlayer, UnityEngine.Time.realtimeSinceStartup);
			}
		}
		
		private static readonly HashSet<string> AnimalPrefabs = new HashSet<string>
		{
			"wolf","boar","bear","polar_bear","stag","deer","chicken","horse","crocodile","panther","tiger","snake"
		};

		private bool IsAnimalKill(string prefabName)
		{
			var lower = prefabName.ToLowerInvariant();
			return AnimalPrefabs.Any(lower.Contains);
		}

		private void OnPlayerDeath(BasePlayer victim, HitInfo info)
		{
			if (!isTournamentRunning || victim == null)
			{
				Puts("[Debug] Tournament is not running or victim is null.");
				return;
			}

			if (recentDeaths.TryGetValue(victim.userID, out var lastDeathTime) &&
				(DateTime.UtcNow - lastDeathTime).TotalSeconds < RecentDeathWindowSeconds)
			{
				Puts($"[Debug] Ignoring duplicate death event for {victim.displayName}. Last death: {lastDeathTime}.");
				return;
			}

			recentDeaths[victim.userID] = DateTime.UtcNow;

			var attacker = info?.InitiatorPlayer;
			
			if (attacker == null || attacker == victim)
			{
				if (lastDamageRecords.TryGetValue(victim.userID, out var record))
				{
					float timeSinceLastHit = UnityEngine.Time.realtimeSinceStartup - record.TimeStamp;

					if (timeSinceLastHit <= 30f)
					{
						attacker = record.Attacker;
						Puts($"[Debug] Overriding final attacker with last real attacker: {attacker?.displayName}");
					}
				}
			}

			var initiatorEntity = info?.Initiator as BaseCombatEntity;

			string attackerName = attacker?.displayName ?? "Unknown";
			string entityName = initiatorEntity?.ShortPrefabName ?? "Unknown";
			ulong ownerId = initiatorEntity?.OwnerID ?? 0;
			string ownerName = ownerId != 0 ? GetPlayerName(ownerId) : "None";

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
                    attackerName = attacker.ShortPrefabName;
                }
                else if (initiatorEntity is BaseNpc npc && !string.IsNullOrEmpty(npc.ShortPrefabName))
                {
                    attackerName = npc.ShortPrefabName;
                }
                else if (initiatorEntity != null && string.IsNullOrEmpty(attackerName))
                {
                    attackerName = initiatorEntity.ShortPrefabName;
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
					"autoturret_deployed"        => "autoturret",
					"guntrap"                    => "guntrap",
					"flameturret.deployed"       => "flameturret",
					"beartrap"                   => "bear trap",
					"wooden_floor_spike_cluster" => "floor spike",
					"landmine"                   => "landmine",
					"barricade"                  => "barricade",
					_                            => entityName
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
					"autoturret_deployed"        => "autoturret",
					"guntrap"                    => "guntrap",
					"flameturret.deployed"       => "flameturret",
					"beartrap"                   => "bear trap",
					"wooden_floor_spike_cluster" => "floor spike",
					"landmine"                   => "landmine",
					"barricade"                  => "barricade",
					_                            => entityName
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

			lastDamageRecords.Remove(victim.userID);

        }

    #endregion

	#region OnEntityDeath

		private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
		{
			if (!isTournamentRunning) return;

			// Handle player kills Heli or Bradley
			if (entity.ShortPrefabName.Contains("helicopter") ||
				entity.ShortPrefabName.Contains("bradley"))
			{
				var killer = info?.InitiatorPlayer;
				if (killer != null && participants.Contains(killer.userID)
					&& Configuration.ScoreRules.TryGetValue("ENT", out int entPts))
				{
					string entName = entity.ShortPrefabName.Contains("helicopter") ? "Helicopter" : "Bradley";
					UpdatePlayerScore(killer.userID, "ENT", $"destroying a {entName}", null, info, entityName: entName);
					Puts($"[Debug] {killer.displayName} earned {entPts} point(s) for downing a {entName}.");
				}
				return;
			}

			// Handle player kills Animal over Long Distance
			var victim = entity as BaseNpc;
			if (victim == null || !IsAnimalKill(victim.ShortPrefabName)) return;

			var killer2 = info?.InitiatorPlayer;
			if (killer2 == null || !participants.Contains(killer2.userID)) return;

			float dist = Vector3.Distance(killer2.transform.position, victim.transform.position);
			if (dist <= Configuration.AnimalKillDistance) return;

			if (Configuration.ScoreRules.TryGetValue("WHY", out int whyPts))
			{
				UpdatePlayerScore(
					killer2.userID,
					"WHY",
					$"killing an animal ({victim.ShortPrefabName}) from over {Configuration.AnimalKillDistance} m away",
					null,
					info);
				Puts($"[Debug] Awarded {whyPts} pt(s) to {killer2.displayName} for {victim.ShortPrefabName} kill at {dist:F1} m");
			}
		}
	
	#endregion

    #region Score Handling

        private string GetPlayerName(ulong userId)
        {
            if (playerNameCache.TryGetValue(userId, out var cachedName) && !string.IsNullOrEmpty(cachedName) && cachedName != "Unknown")
            {
                return cachedName;
            }

            var player = BasePlayer.FindByID(userId) ?? BasePlayer.FindSleeping(userId);
            if (player != null && !string.IsNullOrEmpty(player.displayName))
            {
                playerNameCache[userId] = player.displayName;

                if (participantsData.TryGetValue(userId, out var participant) && participant.Name == "Unknown")
                {
                    participant.Name = player.displayName;
                    SaveParticipantsData();
                }

                return player.displayName;
            }

            if (participantsData.TryGetValue(userId, out var participantFromData) && !string.IsNullOrEmpty(participantFromData.Name) && participantFromData.Name != "Unknown")
            {
                playerNameCache[userId] = participantFromData.Name;
                return participantFromData.Name;
            }

            try
            {
                if (File.Exists(ParticipantsFile))
                {
                    var participantsFromFile = JsonConvert.DeserializeObject<Dictionary<ulong, PlayerStats>>(File.ReadAllText(ParticipantsFile));
                    if (participantsFromFile != null && participantsFromFile.TryGetValue(userId, out var participantFromFile))
                    {
                        if (!string.IsNullOrEmpty(participantFromFile.Name) && participantFromFile.Name != "Unknown")
                        {
                            playerNameCache[userId] = participantFromFile.Name;

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

            return "Unknown";
        }

        private string ParticipantsFile => $"{DataDirectory}/Participants.json";

        private ConcurrentDictionary<ulong, PlayerStats> participantsData = new ConcurrentDictionary<ulong, PlayerStats>();

        private void LoadParticipantsData()
        {
            try
            {
                EnsureDataDirectory();
                if (File.Exists(ParticipantsFile))
                {
                    participantsData = new ConcurrentDictionary<ulong, PlayerStats>(
                        JsonConvert.DeserializeObject<Dictionary<ulong, PlayerStats>>(File.ReadAllText(ParticipantsFile))
                        ?? new Dictionary<ulong, PlayerStats>()
                    );

                    foreach (var participant in participantsData.Values)
                    {
                        if (!string.IsNullOrEmpty(participant.Name))
                        {
                            playerNameCache[participant.UserId] = participant.Name;
                        }
                    }

                    participants.Clear();
                    foreach (var userId in participantsData.Keys)
                    {
                        participants.Add(userId);
                    }

                    Puts($"[Debug] Loaded participants: {string.Join(", ", participants)}");
                }
                else
                {
                    participantsData = new ConcurrentDictionary<ulong, PlayerStats>();
                    participants.Clear();
                    PrintWarning("Participants data file is missing or corrupt. Starting with an empty dataset.");
                    SaveParticipantsData();
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to load participants data: {ex.Message}. Initializing with an empty dictionary.");
                participantsData = new ConcurrentDictionary<ulong, PlayerStats>();
                participants.Clear();
                SaveParticipantsData();
            }
        }

        private Timer saveDataTimer;

        private void StartDataSaveTimer()
        {
            saveDataTimer = timer.Every(300f, SaveParticipantsData);
        }

        private readonly object participantsDataLock = new object();

        private void SaveParticipantsData()
		{
			try
			{
				EnsureDataDirectory();

				string serializedData;
				lock (participantsDataLock)
				{
					serializedData = SerializeParticipantsData(participantsData);
				}

				Puts($"[Debug] Preparing to save Participants.json. Data:\n{serializedData}");

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

				lock (participantsDataLock)
				{
					File.WriteAllText(ParticipantsFile, serializedData);
				}

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
                throw;
            }
        }

        private void UpdatePlayerScore(ulong userId, string actionCode, string actionDescription, BasePlayer victim = null, HitInfo info = null, string attackerName = null, string entityName = "Unknown", bool reverseMessage = false)
		{
			Puts($"[Debug] UpdatePlayerScore called for UserID={userId}, ActionCode={actionCode}, Victim={victim?.displayName ?? "None"}, Entity={entityName}");
			
			if (inactiveParticipants.Contains(userId))
			{
				Puts($"[Debug] Skipping score update: {userId} is inactive.");
				return;
			}

			if (!participantsData.TryGetValue(userId, out var participant))
			{
				PrintWarning($"[Error] Player with UserID {userId} not found in participants. Current participants: {string.Join(", ", participantsData.Keys)}");
				return;
			}

			if (!Configuration.ScoreRules.TryGetValue(actionCode, out int points))
			{
				PrintWarning($"[Error] Invalid action code: {actionCode}. Available codes: {string.Join(", ", Configuration.ScoreRules.Keys)}");
				return;
			}

			int previousScore = participant.Score;
			participant.Score += points;

			Puts($"[Debug] {participant.Name} (UserID: {userId}) | Previous Score: {previousScore} | Gained: {points} | New Score: {participant.Score}");

			SaveParticipantsData();
			
			string savedData = File.ReadAllText(ParticipantsFile);
			Puts($"[Debug] Participants.json after save: {savedData}");

			string pluralS = Math.Abs(points) == 1 ? "" : "s";

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

			if (!Configuration.MessageTemplates.TryGetValue(templateKey, out string template))
			{
				PrintWarning($"[Error] Message template for action code '{actionCode}' not found. Using default.");
				template = Configuration.MessageTemplates["PlayerScoreUpdate"];
			}

			attackerName ??= GetPlayerName(userId);

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

            if (actionCode == "WHY")
            {
                placeholders["Distance"] = Configuration.AnimalKillDistance.ToString();
            }

			string globalMessage = FormatMessage(template, placeholders);
			
			SendTournamentMessage(globalMessage);
			if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
			{
				SendDiscordMessage(globalMessage);
			}

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

            var history = new List<object>();
            if (File.Exists(HistoryFile))
            {
                history = JsonConvert.DeserializeObject<List<object>>(File.ReadAllText(HistoryFile)) ?? new List<object>();
            }
            history.Add(completedTournament);
            File.WriteAllText(HistoryFile, JsonConvert.SerializeObject(history, Formatting.Indented));

            string winnersFile = $"{DataDirectory}/Winners_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            File.WriteAllText(winnersFile, JsonConvert.SerializeObject(completedTournament.Winners, Formatting.Indented));
        }

        private ConcurrentDictionary<ulong, string> playerNameCache = new ConcurrentDictionary<ulong, string>();

        private class PlayerStats
        {
            public ulong UserId { get; }
            public string Name { get; set; }
            public int Score { get; set; }

            public PlayerStats(ulong userId)
            {
                UserId = userId;
                Name = "Unknown";
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

            var playerList = string.Join("\n", topPlayers.Select((p, i) => $"{i + 1}. {p.Name} ({p.Score} Points)"));

            var message = FormatMessage(Configuration.MessageTemplates["TournamentWinners"], new Dictionary<string, string>
            {
                { "PlayerCount", topPlayers.Count.ToString() },
                { "PlayerList", playerList }
            });

            SendTournamentMessage(message);
            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                SendDiscordMessage(message);
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

                var requiredPlaceholders = defaultTemplates[templateKey]
                    .Split(new[] { '{', '}' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where((s, i) => i % 2 == 1)
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
                var formattedMessage = Configuration.ChatFormat.Replace("{message}", message);

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
                string formattedMessage = Configuration.ChatFormat.Replace("{message}", message);

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

			try
			{
				string formattedMessage = Configuration.ChatFormat.Replace("{message}", message);
				Server.Broadcast(formattedMessage, ulong.Parse(Configuration.ChatIconSteamId));
			}
			catch (Exception ex)
			{
				PrintError($"[SendTournamentMessage] Error sending message to all players: {ex.Message}");
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

    [PluginReference]
    private Plugin Kits;
	private Plugin Clans;

	private void NormaliseKitPrices()
	{
		Configuration.KitPrices = new Dictionary<string, int>(
			Configuration.KitPrices, StringComparer.OrdinalIgnoreCase);
	}

	#region Kit Economy Integration
		[HookMethod("OnKitRedeemed")]
		private void OnKitRedeemed(BasePlayer player, string kitName)
		{
			if (player == null || string.IsNullOrWhiteSpace(kitName)) return;
			kitName = kitName.Trim();

			if (!Configuration.KitPrices.TryGetValue(kitName, out int kitPrice))
			{
				PrintWarning($"[KitsEconomy] No price defined for kit: {kitName}");
				return;
			}

			if (!participantsData.TryGetValue(player.userID, out var participant))
				return;

			bool pointsDeducted = false;
			lock (participantsDataLock)
			{
				if (participant.Score >= kitPrice)
				{
					participant.Score -= kitPrice;
					pointsDeducted = true;
				}
			}

			if (!pointsDeducted)
			{
				lock (participantsDataLock)
				{
					participant.Score -= kitPrice;
				}
				SaveParticipantsData();

				PrintWarning($"[RustRoyale] {player.displayName} received kit '{kitName}' without sufficient points. Score forced to {participant.Score}.");
				LogEvent($"{player.displayName} forced into negative score ({participant.Score}) after kit '{kitName}' could not be fully charged.");

				var debtMsg = FormatMessage(
					Configuration.MessageTemplates["KitPurchaseSuccess"],
					new Dictionary<string,string>
					{
						["PlayerName"]  = player.displayName,
						["KitName"]     = kitName,
						["Price"]       = kitPrice.ToString(),
						["TotalPoints"] = participant.Score.ToString()
					}
				);
				SendTournamentMessage(debtMsg);
				if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
					SendDiscordMessage(debtMsg);

				return;
			}

			SaveParticipantsData();

			var successMsg = FormatMessage(
				Configuration.MessageTemplates["KitPurchaseSuccess"],
				new Dictionary<string,string>
				{
					["PlayerName"]  = player.displayName,
					["KitName"]     = kitName,
					["Price"]       = kitPrice.ToString(),
					["TotalPoints"] = participant.Score.ToString()
				}
			);
			SendTournamentMessage(successMsg);
			if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
				SendDiscordMessage(successMsg);

			LogEvent($"{participant.Name} purchased kit '{kitName}' for {kitPrice} points. New Score: {participant.Score}");
		}
	
	#endregion

    #region Commands
        
        [ChatCommand("time_tournament")]
        private void TimeTournamentCommand(BasePlayer player, string command, string[] args)
        {
            string message = GetTimeRemainingMessage();
            SendPlayerMessage(player, message);
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
			if (participantsData.TryGetValue(player.userID, out var existingParticipant))
			{
				if (inactiveParticipants.Contains(player.userID))
				{
					inactiveParticipants.Remove(player.userID);

					string message = $"{player.displayName}, you have rejoined the tournament! Your previous score has been restored.";
					SendPlayerMessage(player, message);
					LogEvent($"{player.displayName} rejoined the tournament with existing score: {existingParticipant.Score}.");
				}
				else
				{
					string message = Configuration.MessageTemplates.TryGetValue("AlreadyParticipating", out var alreadyTemplate)
						? FormatMessage(alreadyTemplate, new Dictionary<string, string>
						{
							{ "PlayerName", player.displayName }
						})
						: $"{player.displayName}, you are already in the tournament.";
					SendPlayerMessage(player, message);
				}
			}
			else
			{
				var newParticipant = new PlayerStats(player.userID) { Name = player.displayName };
				participantsData[player.userID] = newParticipant;
				SaveParticipantsData();

				string message = Configuration.MessageTemplates.TryGetValue("JoinTournament", out var joinTemplate)
					? FormatMessage(joinTemplate, new Dictionary<string, string>
					{
						{ "PlayerName", player.displayName }
					})
					: $"{player.displayName} has joined the tournament.";
				
				SendTournamentMessage(message);
				
				if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
				{
					SendDiscordMessage(message);
				}

				LogEvent($"{player.displayName} joined the tournament as a new participant.");
			}

			participants.Add(player.userID);
		}

        [ChatCommand("exit_tournament")]
		private void ExitTournamentCommand(BasePlayer player, string command, string[] args)
		{
			if (!participantsData.TryGetValue(player.userID, out var participant))
			{
				string message = Configuration.MessageTemplates.TryGetValue("NotInTournament", out var notInTemplate)
					? FormatMessage(notInTemplate, new Dictionary<string, string>
					{
						{ "PlayerName", player.displayName }
					})
					: $"{player.displayName}, you are not part of the tournament.";
				SendPlayerMessage(player, message);
				return;
			}

			inactiveParticipants.Add(player.userID);
			participants.Remove(player.userID);

			int previousScore = participant.Score;

			string leaveMessage;

			if (Configuration.PenaltyOnExitEnabled)
			{
				int penaltyAmount = Configuration.PenaltyPointsOnExit;
				participant.Score -= penaltyAmount;

				Puts($"[Debug] {player.displayName} was penalized {penaltyAmount} points for exiting. Previous Score: {previousScore}, New Score: {participant.Score}");
				LogEvent($"{player.displayName} left the tournament and was penalized {penaltyAmount} points (from {previousScore} to {participant.Score}).");

				leaveMessage = Configuration.MessageTemplates.TryGetValue("LeaveTournamentPenalty", out var penaltyTemplate)
					? FormatMessage(penaltyTemplate, new Dictionary<string, string>
					{
						{ "PlayerName", player.displayName },
						{ "PenaltyPoints", penaltyAmount.ToString() }
					})
					: $"{player.displayName} left the tournament and lost {penaltyAmount} points!";
			}
			else
			{
				LogEvent($"{player.displayName} left the tournament (no penalty applied).");

				leaveMessage = Configuration.MessageTemplates.TryGetValue("LeaveTournament", out var leaveTemplate)
					? FormatMessage(leaveTemplate, new Dictionary<string, string>
					{
						{ "PlayerName", player.displayName }
					})
					: $"{player.displayName} has left the tournament.";
			}

			SaveParticipantsData();

			SendPlayerMessage(player, leaveMessage);
		}

        [ChatCommand("open_tournament")]
		private void OpenTournamentCommand(BasePlayer player, string command, string[] args)
		{
			if (participantsData.TryGetValue(player.userID, out var existingParticipant))
			{
				if (inactiveParticipants.Remove(player.userID))
				{
					Puts($"[Debug] {player.displayName} ({player.userID}) re‑enabled from inactive list.");
					SaveParticipantsData();

					string reactivateMsg = $"{player.displayName}, you’ve re‑joined the tournament. Welcome back!";
					SendPlayerMessage(player, reactivateMsg);
					LogEvent($"{player.displayName} reactivated entry in the tournament.");
				}
				else
				{
					Puts($"[Debug] {player.displayName} ({player.userID}) attempted to opt‑in but is already active.");
					string alreadyMsg = Configuration.MessageTemplates.TryGetValue("AlreadyOptedIn", out var tmp)
						? FormatMessage(tmp, new Dictionary<string,string>{{"PlayerName", player.displayName}})
						: $"{player.displayName}, you are already opted into the tournament.";
					SendPlayerMessage(player, alreadyMsg);
				}
				return;
			}

			var newParticipant = new PlayerStats(player.userID) { Name = player.displayName };
			participantsData[player.userID] = newParticipant;
			inactiveParticipants.Remove(player.userID);
			openTournamentPlayers.Add(player.userID);

			Puts($"[Debug] {player.displayName} ({player.userID}) added to participantsData and openTournamentPlayers.");
			SaveParticipantsData();

			string successMsg = Configuration.MessageTemplates.TryGetValue("JoinTournament", out var joinTpl)
				? FormatMessage(joinTpl, new Dictionary<string,string>{{"PlayerName", player.displayName}})
				: $"{player.displayName} has successfully opted into the tournament.";
			SendPlayerMessage(player, successMsg);

			LogEvent($"{player.displayName} opted into the tournament.");
		}

        [ChatCommand("close_tournament")]
        private void CloseTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (participantsData.ContainsKey(player.userID))
            {
                inactiveParticipants.Add(player.userID);
				autoEnrollBlacklist.Add(player.userID);
                SaveAutoEnrollBlacklist();
                SaveParticipantsData();

                Puts($"[Debug] {player.displayName} ({player.userID}) marked inactive from participantsData and added to auto-enroll blacklist.");

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
                Puts($"[Debug] {player.displayName} ({player.userID}) was not found in participantsData.");
                Puts($"[Debug] Current participantsData: {string.Join(", ", participantsData.Keys.Select(id => $"{id} ({GetPlayerName(id)})"))}");

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
                string timeRemainingMessage = GetTimeRemainingMessage();

                var participants = participantsData.Values.OrderByDescending(p => p.Score).ToList();

                string topScorerMessage = participants.Any()
                    ? $"{participants.First().Name} with {participants.First().Score} points"
                    : "No participants yet.";

                string totalParticipants = participants.Count.ToString();

                var message = $"{timeRemainingMessage}\n" +
                            $"Total participants at this time: {totalParticipants}\n" +
                            $"Current top scorer: {topScorerMessage}";

                SendPlayerMessage(player, message);
            }
            else
            {
                string timeRemainingToStart = tournamentStartTime > DateTime.UtcNow
                    ? FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow)
                    : "N/A";

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
				DisplayScoresAndClans(player);
			}
			catch (Exception ex)
			{
				PrintError($"[Error] Failed to display scores: {ex.Message}");
				SendPlayerMessage(player, "An error occurred while fetching scores. Please try again later.");
			}
		}
		
		private void LoadTeamLeaderNames()
		{
			if (File.Exists(TeamLeadersFile))
			{
				teamLeaderNames = JsonConvert.DeserializeObject<Dictionary<ulong, string>>(File.ReadAllText(TeamLeadersFile)) ?? new();
			}
		}

		private void SaveTeamLeaderNames()
		{
			File.WriteAllText(TeamLeadersFile, JsonConvert.SerializeObject(teamLeaderNames, Formatting.Indented));
		}

		private void DisplayScoresAndClans(BasePlayer player)
		{
			if (!participantsData.Any())
			{
				Puts("[Debug] No participants found to display scores.");
				SendPlayerMessage(player, "No scores are available at the moment. Join the tournament to compete!");
				return;
			}

			var sortedPlayers = participantsData.Values
				.OrderByDescending(p => p.Score)
				.ToList();
			var topPlayers = sortedPlayers.Take(Configuration.TopPlayersToTrack).ToList();

			var clanTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var groupNames = new Dictionary<string, string>();

			foreach (var ps in participantsData.Values)
			{
				string groupKey = GetGroupKey(ps.UserId);

				if (string.IsNullOrEmpty(groupKey) ||
					groupKey.Equals("default", StringComparison.OrdinalIgnoreCase) ||
					groupKey.Equals("No Group", StringComparison.OrdinalIgnoreCase))
					continue;

				string displayName = groupKey;

				if (ulong.TryParse(groupKey, out ulong teamId) &&
					RelationshipManager.ServerInstance.teams.ContainsKey(teamId))
				{
					if (teamLeaderNames.TryGetValue(teamId, out var cachedName))
						{
							displayName = $"{cachedName}'s team";
						}
						else
						{
							displayName = $"Team {teamId}";
						}
				}

				groupNames[groupKey] = displayName;

				if (!clanTotals.ContainsKey(groupKey))
					clanTotals[groupKey] = 0;

				clanTotals[groupKey] += ps.Score;
			}

			var topClans = clanTotals
				.OrderByDescending(kv => kv.Value)
				.Take(Configuration.TopClansToTrack)
				.ToList();

			var sb = new StringBuilder();
			sb.AppendLine("Top Players:");
			for (int i = 0; i < topPlayers.Count; i++)
				sb.AppendLine($"{i + 1}. {topPlayers[i].Name} ({topPlayers[i].Score} pts)");

			if (topClans.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("Top Clans/Groups:");
				for (int i = 0; i < topClans.Count; i++)
				{
					string displayName = groupNames.ContainsKey(topClans[i].Key) ? groupNames[topClans[i].Key] : topClans[i].Key;
					sb.AppendLine($"{i + 1}. {displayName} ({topClans[i].Value} pts)");
				}
			}

			SendPlayerMessage(player, sb.ToString().TrimEnd());
			Puts($"[Debug] Displayed top {Configuration.TopPlayersToTrack} players and top {Configuration.TopClansToTrack} clans for {player.displayName}");
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
                LoadConfig();
                ValidateConfiguration();

                ApplyConfiguration();

                SendPlayerMessage(player, "Configuration has been reloaded successfully and applied.");

                Puts($"[Debug] Configuration reloaded successfully by {player.displayName} ({player.UserIDString}).");

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
                PrintError($"Failed to reload configuration: {ex.Message}");
                SendPlayerMessage(player, "Failed to reload configuration. Check the server logs for details.");
            }
        }

        private void ApplyConfiguration()
        {
            UpdateTimeZoneDependentLogic();

            lastNotifiedMinutes = -1;

            LogConfiguration(Configuration);

            if (isTournamentRunning)
            {
                Puts("A tournament is currently running. Changes to scoring or duration will apply to the next tournament.");
            }

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
            if (Configuration.ScoreRules != null && Configuration.ScoreRules.Count > 0)
            {
                Puts("Scoring rules applied:");
                foreach (var rule in Configuration.ScoreRules)
                {
                    Puts($"  {rule.Key}: {rule.Value} points");
                }
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
				SendPlayerMessage(player, "No tournament is currently running.");
				return;
			}

			EndTournament();

			SendPlayerMessage(player, "Tournament has been ended manually.");
			Puts($"[Debug] Tournament manually ended by {player.displayName} ({player.UserIDString}).");
		}

        [ChatCommand("show_rules")]
		private void ShowRulesCommand(BasePlayer player, string command, string[] args)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Tournament Scoring Rules:");

			foreach (var kvp in Configuration.ScoreRules)
			{
				string line = kvp.Key switch
				{
					"KILL" => $"• Player kill: +{kvp.Value} points",
					"DEAD" => $"• Death by another player: {kvp.Value} points",
					"JOKE" => $"• Self-inflicted or trap death: {kvp.Value} points",
					"NPC"  => $"• NPC kill: +{kvp.Value} points",
					"ENT"  => $"• Helicopter/Bradley kill: +{kvp.Value} points",
					"BRUH" => $"• Death by NPC/vehicle: {kvp.Value} points",
					"WHY"  => $"• Animal long-range kill (> {Configuration.AnimalKillDistance}m): +{kvp.Value} points",
					_      => $"• {kvp.Key}: {kvp.Value} points"
				};
				sb.AppendLine(line);
			}

			sb.AppendLine("\n Kit Prices:");
			foreach (var kit in Configuration.KitPrices)
				sb.AppendLine($"• {kit.Key}: {kit.Value} points");

			SendPlayerMessage(player, sb.ToString());
		}
		
		[ChatCommand("config_tournament")]
		private void ConfigTournamentCommand(BasePlayer player, string cmd, string[] args)
		{
			if (!ValidateAdminCommand(player, "open config")) return;
			ShowConfigUI(player);
		}

        [ChatCommand("help_tournament")]
        private void HelpTournamentCommand(BasePlayer player, string command, string[] args)
        {
            var commandDescriptions = new Dictionary<string, string>
			{
				{ "start_tournament", "Start the tournament now (Admin only)." },
				{ "end_tournament", "End the running tournament early (Admin only)." },
				{ "reload_config", "Reload the tournament config (Admin only)." },
				{ "config_tournament", "Open the configuration UI (Admin only)." },
				{ "enter_tournament", "Join the tournament and compete." },
				{ "exit_tournament", "Leave the tournament (may lose points)." },
				{ "open_tournament", "Opt-in to the tournament manually." },
				{ "close_tournament", "Opt-out and disable auto-enroll." },
				{ "time_tournament", "Check time remaining in current tournament." },
				{ "status_tournament", "Get current tournament status and top player." },
				{ "score_tournament", "View the leaderboard and top clans." },
				{ "show_rules", "See the scoring rules and kit prices." },
				{ "help_tournament", "Show this list of commands." }
			};

            var restrictedCommands = new HashSet<string>
            {
                "start_tournament",
                "end_tournament",
                "reload_config"
            };

            var availableCommands = commandDescriptions.Keys
                .Where(cmd => !restrictedCommands.Contains(cmd) || HasAdminPermission(player))
                .ToList();

            var helpText = "Available Commands:\n";
            foreach (var commandName in availableCommands)
            {
                if (commandDescriptions.TryGetValue(commandName, out var description))
                {
                    helpText += $"- /{commandName}: {description}\n";
                }
            }

            SendPlayerMessage(player, helpText.TrimEnd());
        }

        #endregion
    }
}
