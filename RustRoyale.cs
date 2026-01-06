using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Facepunch;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Newtonsoft.Json;
using Rust;

namespace Oxide.Plugins
{
    [Info("RustRoyale", "Potaetobag", "1.3.5"), Description("Rust Royale custom tournament game mode with point-based scoring system.")]
    class RustRoyale : RustPlugin 
    {
        private bool initialized = false;
        private const string ConfigUiPanelName = "RustRoyale_Config_UI";
        private int _langDownloadsCompleted = 0;
        private const int RequiredLangDownloads = 3;
        private readonly Dictionary<string, string> pendingConfig = new Dictionary<string, string>();
        private readonly Dictionary<ulong, int> npcKillCounts = new();
        private readonly Dictionary<ulong, int> animalKillCounts = new();
        private HashSet<ulong> welcomeOptOut = new HashSet<ulong>();
        private string WelcomeOptOutFile => $"{DataDirectory}/WelcomeOptOut.json";
        private Dictionary<ulong, string> teamLeaderNames = new();
        private string TeamLeadersFile => $"{DataDirectory}/TeamLeaders.json";
        private Lang lang;
        private bool hasRebuiltCaps = false;
        private string WithIndefiniteArticle(string word, string langCode)
        {
            var lang = Interface.Oxide.GetLibrary<Lang>();

            if (string.IsNullOrEmpty(word))
            {
                var unknown = lang.GetMessage("UnknownEntity", this, langCode) ?? "unknown entity";
                if (langCode.StartsWith("en"))
                {
                    char first = unknown.ToLowerInvariant()[0];
                    var fmtTpl = "aeiou".Contains(first)
                        ? lang.GetMessage("ArticleTemplateAE", this, langCode)
                        : lang.GetMessage("ArticleTemplateA",  this, langCode);
                    return string.Format(fmtTpl, unknown);
                }
                else
                {
                    var genericTpl = lang.GetMessage("ArticleTemplate", this, langCode) ?? "a {0}";
                    return string.Format(genericTpl, unknown);
                }
            }

            if (langCode.StartsWith("en"))
            {
                char first = word.ToLowerInvariant()[0];
                var fmtTpl = "aeiou".Contains(first)
                    ? lang.GetMessage("ArticleTemplateAE", this, langCode)
                    : lang.GetMessage("ArticleTemplateA",  this, langCode);
                return string.Format(fmtTpl, word);
            }
            else
            {
                var genericTpl = lang.GetMessage("ArticleTemplate", this, langCode) ?? "a {0}";
                return string.Format(genericTpl, word);
            }
        }
    
    #region Configuration
        private ConfigData Configuration;
        private class ConfigData
        {
            public string DefaultLanguage = "en";
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
            public bool EnableAutoUpdate { get; set; } = false;
            public int PenaltyPointsOnExit { get; set; } = 25;
            public int StartHour { get; set; } = 12;
            public int StartMinute { get; set; } = 0;
            public int DurationHours { get; set; } = 125;
            public int DataRetentionDays { get; set; } = 30;
            public int TopPlayersToTrack { get; set; } = 3;
            public int TopClansToTrack { get; set; } = 3;
            public int JoinCutoffHours { get; set; } = 0; // 0 = No late‑join cut‑off
            public int NpcKillCap { get; set; } = 0;
            public int AnimalKillCap { get; set; } = 0;
            public List<int> NotificationIntervals { get; set; } = new List<int> { 600, 60 }; // Default: every 10 minutes (600 seconds) and last minute (60 seconds)
            public Dictionary<string, int> ScoreRules { get; set; } = new Dictionary<string, int>
            {
                {"KILL", 10},     // Human player kills another human player
                {"DEAD", -15},     // Human player is killed by another human player
                {"JOKE", -10},    // Death by traps, self-inflicted damage
                {"NPC", 1},       // Kill an NPC (Murderer, Zombie, Scientist, Scarecrow)
                {"ENT", 50},      // Kill a Helicopter or Bradley Tank
                {"BRUH", -10},    // Death by an NPC, Helicopter, or Bradley
                {"WHY", 10}       // Award points for killing an animal (wolf, boar, bear, stag, deer) from over Xm away 
            };
            public float AnimalKillDistance { get; set; } = 150f;
            public Dictionary<string, int> KitPrices { get; set; } = new Dictionary<string, int>
            {
                {"Starter", 5},
                {"Bronze", 25},
                {"Silver", 50},
                {"Gold", 75},
                {"Platinum", 100},
                {"Mystery Box", 75}
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

            void SetDefault<T>(Func<T> getter, Action<T> setter, T defaultValue, string label, Func<T, bool> isInvalid)
            {
                if (isInvalid(getter()))
                {
                    PrintWarning($"Invalid {label}. Defaulting to {defaultValue}.");
                    setter(defaultValue);
                    updated = true;
                }
            }

            if (!Enum.TryParse(Configuration.StartDay, true, out DayOfWeek _))
            {
                PrintWarning("Invalid StartDay in configuration. Defaulting to Friday.");
                Configuration.StartDay = "Friday";
                updated = true;
            }

            SetDefault(() => Configuration.PenaltyPointsOnExit, v => Configuration.PenaltyPointsOnExit = v, 25, "PenaltyPointsOnExit", v => v < 0);
            SetDefault(() => Configuration.AnimalKillDistance, v => Configuration.AnimalKillDistance = v, 150f, "AnimalKillDistance", v => v <= 0);
            SetDefault(() => Configuration.StartHour, v => Configuration.StartHour = v, 14, "StartHour", v => v < 0 || v > 23);
            SetDefault(() => Configuration.StartMinute, v => Configuration.StartMinute = v, 0, "StartMinute", v => v < 0 || v > 59);
            SetDefault(() => Configuration.DataRetentionDays, v => Configuration.DataRetentionDays = v, 30, "DataRetentionDays", v => v <= 0);
            SetDefault(() => Configuration.TopPlayersToTrack, v => Configuration.TopPlayersToTrack = v, 3, "TopPlayersToTrack", v => v <= 0);
            SetDefault(() => Configuration.NpcKillCap, v => Configuration.NpcKillCap = v, 0, "NpcKillCap", v => v < 0);
            SetDefault(() => Configuration.AnimalKillCap, v => Configuration.AnimalKillCap = v, 0, "AnimalKillCap", v => v < 0);
            SetDefault(() => Configuration.TopClansToTrack, v => Configuration.TopClansToTrack = v, 3, "TopClansToTrack", v => v <= 0);
            SetDefault(() => Configuration.JoinCutoffHours, v => Configuration.JoinCutoffHours = 6, 6, "JoinCutoffHours", v => v < 0 || v > Configuration.DurationHours);

            if (Configuration.DurationHours < 0.0167 || Configuration.DurationHours > 872)
            {
                PrintWarning($"Invalid DurationHours: {Configuration.DurationHours}. Defaulting to 72.");
                Configuration.DurationHours = 72;
                updated = true;
            }
            else if (Configuration.DurationHours < 1)
            {
                Puts($"[Debug] Short tournament duration detected: {Configuration.DurationHours} hours. This is valid.");
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
            
            if (!typeof(bool).IsAssignableFrom(Configuration.EnableAutoUpdate.GetType()))
            {
                PrintWarning("EnableAutoUpdate is invalid or missing. Defaulting to false.");
                Configuration.EnableAutoUpdate = false;
                updated = true;
            }

            if (Configuration.KitPrices == null || Configuration.KitPrices.Count == 0)
            {
                PrintWarning("KitPrices missing or empty. Using defaults.");
                Configuration.KitPrices = new Dictionary<string, int>
                {
                    {"Starter", 5}, {"Bronze", 25}, {"Silver", 50},
                    {"Gold", 75}, {"Platinum", 100}, {"Mystery Box", 50}
                };
                updated = true;
            }

            if (Configuration.NotificationIntervals == null || !Configuration.NotificationIntervals.Any())
            {
                PrintWarning("NotificationIntervals is invalid or missing. Defaulting to every 10 minutes and the last minute.");
                Configuration.NotificationIntervals = new List<int> { 600, 60 };
                updated = true;
            }
            else
            {
                var cleaned = Configuration.NotificationIntervals.Where(x => x > 0).Distinct().ToList();
                if (cleaned.Count != Configuration.NotificationIntervals.Count)
                {
                    PrintWarning("NotificationIntervals contained invalid or duplicate values. Cleaning up the list.");
                    Configuration.NotificationIntervals = cleaned;
                    updated = true;
                }
            }

            if (Configuration.ShowWelcomeUI != true && Configuration.ShowWelcomeUI != false)
            {
                PrintWarning("Invalid ShowWelcomeUI. Defaulting to true.");
                Configuration.ShowWelcomeUI = true;
                updated = true;
            }

            if (updated)
            {
                SaveConfig();
                Puts("Configuration updated with validated defaults.");
            }

            Puts($"Configuration validated: StartDay={Configuration.StartDay}, StartHour={Configuration.StartHour}, DurationHours={Configuration.DurationHours}, " +
                 $"DataRetentionDays={Configuration.DataRetentionDays}, TopPlayersToTrack={Configuration.TopPlayersToTrack}, " +
                 $"TopClansToTrack={Configuration.TopClansToTrack}, NotificationIntervals={string.Join(", ", Configuration.NotificationIntervals)}.");
        }
    #endregion
    #region Plugin Handle
        private void OnUnload()
        {
            if (saveDataTimer != null)
            {
                saveDataTimer.Destroy();
                saveDataTimer = null;
            }

            CleanupTimers();

            SaveParticipantsData();
            SavePlayerLanguages();
        }

        private void CleanupTimers()
        {
            if (countdownTimer != null)
            {
                countdownTimer.Destroy();
                countdownTimer = null;
            }
            if (tournamentDurationTimer != null)
            {
                tournamentDurationTimer.Destroy();
                tournamentDurationTimer = null;
            }
        }
    #endregion
    #region Language
        private Dictionary<ulong, string> playerLanguages = new();

        private void SavePlayerLanguages() =>
            Interface.Oxide.DataFileSystem.WriteObject("RustRoyale_PlayerLanguages", playerLanguages);

        private void LoadPlayerLanguages() =>
            playerLanguages = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, string>>("RustRoyale_PlayerLanguages");

        private string GetPlayerLang(BasePlayer player)
        {
            if (playerLanguages.TryGetValue(player.userID, out var lang) && Translations.ContainsKey(lang))
                return lang;

            return Configuration.DefaultLanguage;
        }

        private string Lang(string key, BasePlayer player = null, Dictionary<string, string> tokens = null, string langOverride = null)
        {
            string lang = langOverride ?? (player != null ? GetPlayerLang(player) : Configuration.DefaultLanguage);

            if (!Translations.TryGetValue(lang, out var dict) || !dict.TryGetValue(key, out var message))
            {
                if (!Translations.TryGetValue("en", out var fallbackDict) || !fallbackDict.TryGetValue(key, out message))
                {
                    Puts($"[Warning] Missing translation key '{key}' in '{lang}' and fallback 'en'");
                    return key;
                }
            }

            if (tokens != null)
            {
                foreach (var token in tokens)
                    message = message.Replace("{" + token.Key + "}", token.Value);
            }

            return message;
        }
        
        private void EnsureLatestLangFile(string langCode)
        {
            string url = $"https://raw.githubusercontent.com/potaetobag/RustRoyale/main/lang/{langCode}/RustRoyale.json";
            string fullPath = Path.Combine(Interface.Oxide.RootDirectory, "oxide", "lang", langCode, "RustRoyale.json");

            webrequest.Enqueue(url, null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response))
                {
                    Puts($"[LangLoader] Failed to fetch '{langCode}' language file. HTTP Code: {code}");
                }
                else
                {
                    try
                    {
                        bool shouldWrite = true;

                        if (File.Exists(fullPath))
                        {
                            string currentContent = File.ReadAllText(fullPath);
                            shouldWrite = currentContent.Trim() != response.Trim();
                        }

                        if (shouldWrite)
                        {
                            string dir = Path.GetDirectoryName(fullPath);
                            if (!Directory.Exists(dir))
                                Directory.CreateDirectory(dir);

                            Puts($"[LangLoader] Attempting to write to: {fullPath}");
                            File.WriteAllText(fullPath, response);
                            Puts($"[LangLoader] Updated language file: oxide/lang/{langCode}/RustRoyale.json");
                        }
                        else
                        {
                            Puts($"[LangLoader] oxide/lang/{langCode}/RustRoyale.json is already up to date.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Puts($"[LangLoader] Error saving oxide/lang/{langCode}/RustRoyale.json: {ex.Message}");
                    }
                }

                _langDownloadsCompleted++;
                Puts($"[LangLoader] Completed {_langDownloadsCompleted}/{RequiredLangDownloads} language downloads.");

                if (_langDownloadsCompleted >= RequiredLangDownloads)
                {
                    LoadTranslations();
                    Puts("[LangLoader] All language files downloaded. Continuing initialization.");
                    ContinueInitialization();
                }

            }, this);
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
        private void AddCuiLabel(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string text, int fontSize = 13, TextAnchor align = TextAnchor.MiddleLeft)
        {
            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Text = { Text = text, FontSize = fontSize, Align = align }
            }, parent);
        }

        private void AddCuiPanel(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string color, string name = null)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent, name);
        }

        private void AddCuiButton(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string text, string command, string color = "0.5 0.5 0.5 1", int fontSize = 13)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command, Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Text = { Text = text, FontSize = fontSize, Align = TextAnchor.MiddleCenter }
            }, parent);
        }

        private void AddCuiRawImage(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string url)
        {
            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiRawImageComponent { Url = url },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax }
                }
            });
        }
        
        private void LoadWelcomeOptOut()
        {
            if (File.Exists(WelcomeOptOutFile))
            {
                welcomeOptOut = JsonConvert.DeserializeObject<HashSet<ulong>>(File.ReadAllText(WelcomeOptOutFile)) ?? new HashSet<ulong>();
            }
        }

        private void SaveWelcomeOptOut()
        {
            EnsureDataDirectory();
            File.WriteAllText(WelcomeOptOutFile, JsonConvert.SerializeObject(welcomeOptOut, Formatting.Indented));
        }
        
        private const string WelcomeUiPanelName = "RustRoyale_Welcome_UI";

        private void ShowWelcomeUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, WelcomeUiPanelName);
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image        = { Color = "0 0 0 0.6" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", WelcomeUiPanelName);

            container.Add(new CuiPanel
            {
                Image        = { Color = "0.1 0.1 0.1 0.95" },
                RectTransform = { AnchorMin = "0.15 0.1", AnchorMax = "0.85 0.9" }
            }, WelcomeUiPanelName, $"{WelcomeUiPanelName}.main");

            string scrollView = $"{WelcomeUiPanelName}.scroll";
            container.Add(new CuiElement
            {
                Name   = scrollView,
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
                    Text          = { Text = text, FontSize = size, Align = TextAnchor.UpperLeft }
                }, scrollView);
                y -= 0.06f;
            }

            AddCuiRawImage(container, scrollView,
                           "0.4 0.91", "0.6 0.98",
                           "https://panels.twitch.tv/panel-1025512005-image-9ae6176b-7901-47e0-a0a2-2c16fed78df3");
            y -= 0.07f;

            AddTextBlock(Lang("WelcomeTitle",       player), 18);
            AddTextBlock(Lang("WelcomeDescription", player));
            y -= 0.1f;
            AddTextBlock(Lang("WelcomeDiscord",     player));
            y -= 0.05f;

            float baseY    = y;
            float colH     = 0.035f;
            float colGap   = 0.005f;
            float rowSize  = colH + colGap;
            int   maxRows  = Math.Max(Math.Max(3, Configuration.ScoreRules.Count), Configuration.KitPrices.Count);

            float col1Min = 0f,    col1Max = 0.32f;
            float col2Min = 0.34f, col2Max = 0.66f;
            float col3Min = 0.68f, col3Max = 1f;

            AddCuiLabel(container, scrollView,
                        $"{col1Min} {baseY}", $"{col1Max} {baseY + colH}",
                        $"<b>{Lang("StatsHeader",      player)}</b>");
            AddCuiLabel(container, scrollView,
                        $"{col2Min} {baseY}", $"{col2Max} {baseY + colH}",
                        $"<b>{Lang("RulesHeader",      player)}</b>");
            AddCuiLabel(container, scrollView,
                        $"{col3Min} {baseY}", $"{col3Max} {baseY + colH}",
                        $"<b>{Lang("KitPricesHeader",  player)}</b>");

            y = baseY - rowSize;

            for (int i = 0; i < maxRows; i++)
            {
                if (i < 3)
                {
                    string statText = "";
                    if (i == 0)
                    {
                        statText = string.Format(
                            Lang("Duration_Label", player),
                            Configuration.DurationHours);
                    }
                    else if (i == 1)
                    {
                        string timeLeft;
                        if (tournamentStartTime > DateTime.MinValue)
                        {
                            var end  = tournamentStartTime.AddHours(Configuration.DurationHours);
                            var span = end - DateTime.UtcNow;
                            timeLeft = span.TotalSeconds > 0
                                       ? $"{(int)span.TotalHours}h {span.Minutes}m"
                                       : Lang("TournamentEnded", player);
                        }
                        else timeLeft = Lang("NotAvailable", player);

                        statText = string.Format(
                            Lang("TimeLeft_Label", player),
                            timeLeft);
                    }
                    else if (i == 2)
                    {
                        statText = string.Format(
                            Lang("Participants_Label", player),
                            participants.Count);
                    }

                    AddCuiLabel(container, scrollView,
                                $"{col1Min} {y - colH}", $"{col1Max} {y}",
                                $"• {statText}", 13);
                }

                if (i < Configuration.ScoreRules.Count)
                {
                    var kv       = Configuration.ScoreRules.ElementAt(i);
                    string key   = kv.Key;
                    int    value = kv.Value;
                    string friendly = "";
                    string cap      = "";

                    switch (key)
                    {
                        case "KILL":
                            friendly = Lang("Kill_Friendly", player);
                            break;
                        case "DEAD":
                            friendly = Lang("Dead_Friendly", player);
                            break;
                        case "JOKE":
                            friendly = Lang("Joke_Friendly", player);
                            break;
                        case "ENT":
                            friendly = Lang("Ent_Friendly", player);
                            break;
                        case "BRUH":
                            friendly = Lang("Bruh_Friendly", player);
                            break;
                        case "NPC":
                            if (Configuration.NpcKillCap > 0)
                                cap = string.Format(Lang("MaxCapFormat", player), Configuration.NpcKillCap);
                            else
                                cap = Lang("NoCap", player);
                            friendly = string.Format(Lang("NpcLabelFormat", player), cap);
                            break;
                        case "WHY":
                            if (Configuration.AnimalKillCap > 0)
                                cap = string.Format(Lang("MaxCapFormat", player), Configuration.AnimalKillCap);
                            else
                                cap = Lang("NoCap", player);
                            friendly = string.Format(
                                Lang("AnimalKillFormat", player),
                                Configuration.AnimalKillDistance, cap);
                            break;
                        default:
                            friendly = key;
                            break;
                    }

                    string tpl      = Lang("ScoreRuleLine", player);
                    string lineText = string.Format(tpl, friendly, value);

                    AddCuiLabel(container, scrollView,
                                $"{col2Min} {y - colH}", $"{col2Max} {y}",
                                lineText, 13);
                }

                if (i < Configuration.KitPrices.Count)
                {
                    var kit = Configuration.KitPrices.ElementAt(i);
                    string kitName = Lang($"Kit_{kit.Key}", player) ?? kit.Key;
                    string tpl     = Lang("Kit_Label", player);
                    string text    = string.Format(tpl, kitName, kit.Value);

                    AddCuiLabel(container, scrollView,
                                $"{col3Min} {y - colH}", $"{col3Max} {y}",
                                $"• {text}", 13);
                }

                y -= rowSize;
            }

            string toggleLabel = welcomeOptOut.Contains(player.userID)
                ? Lang("Toggle_ShowNextTime", player)
                : Lang("Toggle_DontShowAgain", player);
            string toggleCmd   = $"welcomeui_toggle {player.userID}";
            container.Add(new CuiButton
            {
                Button        = { Command = toggleCmd, Color = "0.2 0.5 0.8 1" },
                RectTransform = { AnchorMin = "0.35 0.01", AnchorMax = "0.49 0.06" },
                Text          = { Text = toggleLabel, FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, $"{WelcomeUiPanelName}.main");

            AddCuiButton(container, $"{WelcomeUiPanelName}.main",
                         "0.51 0.01", "0.65 0.06",
                         Lang("Button_Close", player),
                         "welcomeui_close",
                         "0.7 0.2 0.2 1");

            y -= 0.07f;

            AddTextBlock(Lang("HelpCommands_Text", player));

            var pluginTitles = Interface.Oxide.RootPluginManager
                                 .GetPlugins()
                                 .Select(p => p.Title)
                                 .Where(t => !string.IsNullOrEmpty(t))
                                 .Distinct();
            AddTextBlock($"Plugins: {string.Join(", ", pluginTitles)}");

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
        
        void AddCuiInput(CuiElementContainer container, string parent, string key, string label, string value, float xLabel, float xInput, float y)
        {
            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = $"{xLabel} {y}", AnchorMax = $"{xLabel + 0.2f} {y + 0.035f}" },
                Text = { Text = label, FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, parent);

            container.Add(new CuiPanel
            {
                Image = { Color = "0.8 0.8 0.8 0.3" },
                RectTransform = { AnchorMin = $"{xInput} {y}", AnchorMax = $"{xInput + 0.2f} {y + 0.035f}" }
            }, parent);

            container.Add(new CuiElement
            {
                Name = $"input_{key}",
                Parent = parent,
                Components = {
                    new CuiRectTransformComponent { AnchorMin = $"{xInput} {y}", AnchorMax = $"{xInput + 0.2f} {y + 0.035f}" },
                    new CuiInputFieldComponent {
                        Text = value,
                        Align = TextAnchor.MiddleCenter,
                        Command = $"config_set {key}"
                    }
                }
            });
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

            AddCuiLabel(container, $"{ConfigUiPanelName}.header", "0.02 0", "0.9 1", "RustRoyale Configuration", 18);

            container.Add(new CuiButton
            {
                Button = { Color = "0.8 0.2 0.2 1", Command = "config_cancel" },
                RectTransform = { AnchorMin = "0.93 0.1", AnchorMax = "0.98 0.9" },
                Text = { Text = "<b>X</b>", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, $"{ConfigUiPanelName}.header");

            float y = 0.81f;
            float xLeftLabel = 0.05f, xLeftInput = 0.25f;
            float xRightLabel = 0.50f, xRightInput = 0.70f;

            void AddToggle(string label, string key, bool value, float xLabel, float xButton, float y)
            {
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = $"{xLabel} {y}", AnchorMax = $"{xLabel + 0.2f} {y + 0.035f}" },
                    Text = { Text = label, FontSize = 13, Align = TextAnchor.MiddleCenter }
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
                    Text = { Text = value.ToString(), FontSize = 13, Align = TextAnchor.MiddleCenter }
                }, $"{ConfigUiPanelName}.main");
            }

            AddCuiInput(container, $"{ConfigUiPanelName}.main", "Timezone", "Server Timezone", Configuration.Timezone, xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "StartDay", "Tournament Start Day", Configuration.StartDay, xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "StartHour", "Start Hour (0–23)", Configuration.StartHour.ToString(), xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "StartMinute", "Start Minute (0–59)", Configuration.StartMinute.ToString(), xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "DurationHours", "Tournament Duration (Hours)", Configuration.DurationHours.ToString(), xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "TopPlayersToTrack", "Top Players to Track", Configuration.TopPlayersToTrack.ToString(), xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "TopClansToTrack", "Top Clans to Track", Configuration.TopClansToTrack.ToString(), xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "JoinCutoffHours", "Join Cutoff Time (Hours)", Configuration.JoinCutoffHours.ToString(), xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "AnimalKillDistance", "Animal Kill Distance (m)", Configuration.AnimalKillDistance.ToString(), xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "NpcKillCap", "Max NPC Kills Per Player (0 = Unlimited)", Configuration.NpcKillCap.ToString(), xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "AnimalKillCap", "Max Animal Kills Per Player (0 = Unlimited)", Configuration.AnimalKillCap.ToString(), xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddCuiInput(container, $"{ConfigUiPanelName}.main", "PenaltyPointsOnExit", "Penalty Points on Exit", Configuration.PenaltyPointsOnExit.ToString(), xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            
            AddToggle("Enable Plugin Auto-Update", "EnableAutoUpdate", Configuration.EnableAutoUpdate, xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddToggle("Show Welcome Message", "ShowWelcomeUI", Configuration.ShowWelcomeUI, xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;
            AddToggle("Auto-Start Tournament", "AutoStartEnabled", Configuration.AutoStartEnabled, xLeftLabel, xLeftInput + 0.02f, y); y -= 0.05f;

            float toggleY = 0.81f;
            
            string currentLang = Configuration.DefaultLanguage;
            int currentIndex = Array.IndexOf(SupportedLanguages, currentLang);
            int nextIndex = (currentIndex + 1) % SupportedLanguages.Length;
            string nextLang = SupportedLanguages[nextIndex];

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = $"{xRightLabel} {toggleY}", AnchorMax = $"{xRightLabel + 0.2f} {toggleY + 0.035f}" },
                Text = { Text = "Default Language", FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, $"{ConfigUiPanelName}.main");

            container.Add(new CuiButton
            {
                Button = {
                    Color = "0.2 0.4 0.8 1",
                    Command = $"config_set DefaultLanguage {nextLang}"
                },
                RectTransform = { AnchorMin = $"{xRightInput + 0.02f} {toggleY}", AnchorMax = $"{xRightInput + 0.22f} {toggleY + 0.035f}" },
                Text = { Text = currentLang, FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, $"{ConfigUiPanelName}.main");

            toggleY -= 0.05f;


            AddToggle("Auto-Enroll Players", "AutoEnrollEnabled", Configuration.AutoEnrollEnabled, xRightLabel, xRightInput + 0.02f, toggleY); toggleY -= 0.05f;
            AddToggle("Penalize Players Who Leave", "PenaltyOnExitEnabled", Configuration.PenaltyOnExitEnabled, xRightLabel, xRightInput + 0.02f, toggleY); toggleY -= 0.05f;
            
            y -= 0.05f;

            float groupY = 0.66f;

            foreach (var kv in Configuration.ScoreRules)
            {
                float marginX = xRightLabel + 0.22f;

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = $"{xRightLabel} {groupY}", AnchorMax = $"{xRightLabel + 0.2f} {groupY + 0.035f}" },
                    Text = { Text = $"ScoreRules[{kv.Key}]", FontSize = 13, Align = TextAnchor.MiddleCenter }
                }, $"{ConfigUiPanelName}.main");

                container.Add(new CuiPanel
                {
                    Image = { Color = "0.8 0.8 0.8 0.3" },
                    RectTransform = { AnchorMin = $"{marginX} {groupY}", AnchorMax = $"{marginX + 0.2f} {groupY + 0.035f}" }
                }, $"{ConfigUiPanelName}.main");

                container.Add(new CuiElement
                {
                    Name = $"ScoreRule_{kv.Key}",
                    Parent = $"{ConfigUiPanelName}.main",
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = $"{marginX} {groupY}", AnchorMax = $"{marginX + 0.2f} {groupY + 0.035f}" },
                        new CuiInputFieldComponent {
                            Text = kv.Value.ToString(),
                            Align = TextAnchor.MiddleCenter,
                            Command = $"config_set ScoreRules.{kv.Key}"
                        }
                    }
                });

                groupY -= 0.05f;
            }

            groupY -= 0.01f;

            foreach (var kv in Configuration.KitPrices)
            {
                float marginX = xRightLabel + 0.22f;

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = $"{xRightLabel} {groupY}", AnchorMax = $"{xRightLabel + 0.2f} {groupY + 0.035f}" },
                    Text = { Text = $"KitPrices[{kv.Key}]", FontSize = 13, Align = TextAnchor.MiddleCenter }
                }, $"{ConfigUiPanelName}.main");

                container.Add(new CuiPanel
                {
                    Image = { Color = "0.8 0.8 0.8 0.3" },
                    RectTransform = { AnchorMin = $"{marginX} {groupY}", AnchorMax = $"{marginX + 0.2f} {groupY + 0.035f}" }
                }, $"{ConfigUiPanelName}.main");

                container.Add(new CuiElement
                {
                    Name = $"KitPrice_{kv.Key}",
                    Parent = $"{ConfigUiPanelName}.main",
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = $"{marginX} {groupY}", AnchorMax = $"{marginX + 0.2f} {groupY + 0.035f}" },
                        new CuiInputFieldComponent {
                            Text = kv.Value.ToString(),
                            Align = TextAnchor.MiddleCenter,
                            Command = $"config_set KitPrices.{kv.Key}"
                        }
                    }
                });

                groupY -= 0.05f;
            }

            container.Add(new CuiButton
            {
                Button = { Command = "config_save", Color = "0.2 0.6 0.2 1" },
                RectTransform = { AnchorMin = "0.27 0.02", AnchorMax = "0.47 0.07" },
                Text = { Text = Lang("ConfigSave"), FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, $"{ConfigUiPanelName}.main");

            container.Add(new CuiButton
            {
                Button = { Command = "config_cancel", Color = "0.6 0.2 0.2 1" },
                RectTransform = { AnchorMin = "0.53 0.02", AnchorMax = "0.73 0.07" },
                Text = { Text = Lang("ConfigCancel"), FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, $"{ConfigUiPanelName}.main");
            
            string currentVersion = GetCurrentPluginVersion();
            string versionStatus = "Up to date";
            string versionColor = "#00ff00";

            if (!string.IsNullOrEmpty(LatestPluginVersion))
            {
                var current = ParseVersion(currentVersion);
                var latest = ParseVersion(LatestPluginVersion);

                if (latest > current)
                {
                    versionStatus = $"Update available ({LatestPluginVersion})";
                    versionColor = "#ff0000";
                }
            }

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.20 0.84", AnchorMax = "0.80 0.93" },
                Text = {
                    Text = $"<color={versionColor}>RustRoyale v{currentVersion} - {versionStatus}</color>",
                    FontSize = 13,
                    Align = TextAnchor.MiddleCenter
                }
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
            if (args.Length < 1) return;

            string key = args[0];

            if (key == "DefaultLanguage")
            {
                string current = Configuration.DefaultLanguage;
                int currentIndex = Array.IndexOf(SupportedLanguages, current);
                int nextIndex = (currentIndex + 1) % SupportedLanguages.Length;
                string next = SupportedLanguages[nextIndex];

                Configuration.DefaultLanguage = next;
                player.ChatMessage($"<color=#ffd479>RustRoyale:</color> Default language changed to: {next}");

                ShowConfigUI(player);
                return;
            }

            if (args.Length < 2) return;
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
                try
                {
                    if (kv.Key.StartsWith("ScoreRules."))
                    {
                        string ruleKey = kv.Key.Substring("ScoreRules.".Length);
                        if (Configuration.ScoreRules.ContainsKey(ruleKey) && int.TryParse(kv.Value, out int score))
                        {
                            Configuration.ScoreRules[ruleKey] = score;
                            player.ChatMessage($"Updated ScoreRules[{ruleKey}] = {score}");
                        }
                        else
                        {
                            player.ChatMessage($"Failed to update ScoreRules[{ruleKey}]");
                        }
                        continue;
                    }

                    if (kv.Key.StartsWith("KitPrices."))
                    {
                        string kitKey = kv.Key.Substring("KitPrices.".Length);
                        if (Configuration.KitPrices.ContainsKey(kitKey) && int.TryParse(kv.Value, out int price))
                        {
                            Configuration.KitPrices[kitKey] = price;
                            player.ChatMessage($"Updated KitPrices[{kitKey}] = {price}");
                        }
                        else
                        {
                            player.ChatMessage($"Failed to update KitPrices[{kitKey}]");
                        }
                        continue;
                    }

                    var prop = typeof(ConfigData).GetProperty(kv.Key);
                    if (prop == null)
                    {
                        player.ChatMessage($"Unknown property: {kv.Key}");
                        continue;
                    }

                    object converted;
                    if (prop.PropertyType == typeof(bool))
                    {
                        converted = kv.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (prop.PropertyType.IsEnum)
                    {
                        converted = Enum.Parse(prop.PropertyType, kv.Value, true);
                    }
                    else
                    {
                        converted = Convert.ChangeType(kv.Value, prop.PropertyType);
                    }

                    prop.SetValue(Configuration, converted);
                    player.ChatMessage(Lang("ConfigUpdated", player, new Dictionary<string, string>
                    {
                        { "Key", kv.Key },
                        { "Value", converted.ToString() }
                    }));
                }
                catch (Exception ex)
                {
                    player.ChatMessage(Lang("ConfigParseError", player, new Dictionary<string, string>
                    {
                        { "Key", kv.Key },
                        { "Error", ex.Message }
                    }));
                }
            }

            pendingConfig.Clear();
            SaveConfig();
            ApplyConfiguration();

            CloseConfigUI(player);
            if (Configuration.AutoStartEnabled)
            {
                ScheduleTournament();
            }

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
            LoadPlayerLanguages();

            EnsureLatestLangFile("en");
            EnsureLatestLangFile("es");
            EnsureLatestLangFile("fr");
            
            GetServerIPAndStartTimer();

            initialized = false;
        }

        private void ContinueInitialization()
        {
            LoadTranslations();
            initialized = true;

            LoadWelcomeOptOut();
            ValidateConfiguration();
            NormaliseKitPrices();
            LoadAutoEnrollBlacklist();
            
            LoadParticipantsData();
            LoadTeamLeaderNames();
            
            bool anyNamedParticipant = participantsData.Values
                                 .Any(p => !string.Equals(p.Name, "Unknown", StringComparison.OrdinalIgnoreCase));
            if (anyNamedParticipant && !hasRebuiltCaps)
            {
                RebuildKillCapsFromHistory();
                hasRebuiltCaps = true;
            }
            
            ResumeInterruptedTournament();
            if (!isTournamentRunning)
                ScheduleTournament();
            
            foreach (var p in BasePlayer.activePlayerList)
            {
                try { OnPlayerInit(p); } catch { /* swallow to avoid init loop breaks */ }
            }

            Puts($"Initialization complete. Tournament duration set to {Configuration.DurationHours} hours.");
        }

        private bool ValidateAdminCommand(BasePlayer player, string actionName)
        {
            if (!HasAdminPermission(player))
            {
                Notify("NoPermission", player, extraPlaceholders: new Dictionary<string, string>
                {
                    { "ActionName", actionName }
                });
                return false;
            }
            return true;
        }
    #endregion
    #region API
        void GetServerIPAndStartTimer()
        {
            string ipApiUrl = "http://ip-api.com/json";

            Puts("[RustRoyale] 🌐 Getting public IP...");

            webrequest.Enqueue(ipApiUrl, null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response))
                {
                    Puts("[RustRoyale] ❌ Failed to get public IP.");
                    return;
                }

                try
                {
                    var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                    if (data.TryGetValue("query", out object ipObj))
                    {
                        string ip = ipObj.ToString();
                        Puts($"[RustRoyale] 🛰 Public IP: {ip}");

                        SendServerStatusToApi(ip);
                        timer.Every(3600f, () => SendServerStatusToApi(ip));
                    }
                    else
                    {
                        Puts("[RustRoyale] ⚠️ IP not found in response.");
                    }
                }
                catch (Exception ex)
                {
                    Puts($"[RustRoyale] ⚠️ Error parsing IP JSON: {ex.Message}");
                }
            }, this);
        }

        void SendServerStatusToApi(string ip)
        {
            string apiUrl = "https://api.rustroyale.com/server.php";

            int active = BasePlayer.activePlayerList.Count;
            int sleeping = BasePlayer.sleepingPlayerList.Count;
            int totalPlayers = active + sleeping;
            int maxPlayers = ConVar.Server.maxplayers;

            var payload = new
            {
                name = ConVar.Server.hostname,
                ip = ip,
                players = $"{totalPlayers}/{maxPlayers}",
                status = "online"
            };

            string json = JsonConvert.SerializeObject(payload);

            Puts($"[RustRoyale] 🚀 Sending server status: {json} (Active: {active}, Sleepers: {sleeping}, Max: {maxPlayers})");

            webrequest.Enqueue(apiUrl, json, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response))
                {
                    Puts($"[RustRoyale] ❌ Failed to update server status. Code: {code}, Response: {response ?? "null"}");
                }
                else
                {
                    Puts("[RustRoyale] ✅ Server status updated successfully.");
                }
            }, this, RequestMethod.POST, new Dictionary<string, string>
            {
                { "Content-Type", "application/json" }
            });
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
                EnsureDataDirectory();
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
        private const string PluginUpdateUrl = "https://raw.githubusercontent.com/potaetobag/RustRoyale/main/RustRoyale.cs";
        private string LatestPluginVersion = null;

        private string GetCurrentPluginVersion()
        {
            var attr = (InfoAttribute)Attribute.GetCustomAttribute(typeof(RustRoyale), typeof(InfoAttribute));
            return attr != null ? attr.Version.ToString() : "unknown";
        }
        
        private VersionNumber ParseVersion(string version)
        {
            var parts = version.Split('.');
            if (parts.Length != 3) return new VersionNumber(0, 0, 0);

            return new VersionNumber(
                int.TryParse(parts[0], out var major) ? major : 0,
                int.TryParse(parts[1], out var minor) ? minor : 0,
                int.TryParse(parts[2], out var patch) ? patch : 0
            );
        }

        private string ExtractVersionFromSource(string source)
        {
            var match = Regex.Match(source, @"\[Info\(\s*""[^""]+"",\s*""[^""]+"",\s*""([^""]+)""\)");
            return match.Success ? match.Groups[1].Value : null;
        }
        
        private Dictionary<string, Dictionary<string, string>> Translations = new();
        private readonly string[] SupportedLanguages = new[] { "en", "es", "fr" };

        private void LoadTranslations()
        {
            Translations = new Dictionary<string, Dictionary<string, string>>();

            foreach (var lang in SupportedLanguages)
            {
                string path = Path.Combine(Interface.Oxide.RootDirectory, "oxide", "lang", lang, "RustRoyale.json");

                if (!File.Exists(path))
                {
                    Puts($"[Warning] Language file not found for '{lang}' at path '{path}'");
                    continue;
                }

                try
                {
                    var content = File.ReadAllText(path);
                    var entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);

                    if (entries != null && entries.Count > 0)
                    {
                        Translations[lang] = entries;
                        Puts($"[LangLoader] Loaded {entries.Count} translations for '{lang}'");
                    }
                    else
                    {
                        Puts($"[Warning] Translation file '{lang}' is empty or invalid.");
                    }
                }
                catch (Exception ex)
                {
                    Puts($"[LangLoader] Error loading translations for '{lang}': {ex.Message}");
                }
            }
        }
        
        private string ForceLabelToDefaultLanguage(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;

            var def = string.IsNullOrEmpty(Configuration.DefaultLanguage) ? "en" : Configuration.DefaultLanguage;

            if (Translations != null && Translations.TryGetValue(def, out var defDict) && defDict != null)
            {
                if (defDict.Values.Contains(label))
                    return label;

                foreach (var kvLang in Translations)
                {
                    var dict = kvLang.Value;
                    if (dict == null) continue;

                    foreach (var kv in dict)
                    {
                        if (string.Equals(kv.Value, label, StringComparison.OrdinalIgnoreCase))
                        {
                            if (defDict.TryGetValue(kv.Key, out var defValue) && !string.IsNullOrEmpty(defValue))
                                return defValue;
                        }
                    }
                }
            }

            return label;
        }

        private void OnServerInitialized()
        {
            Puts("[RustRoyale] Checking current version...");
            string currentVersion = GetCurrentPluginVersion();
            Puts($"[RustRoyale] Current plugin version: {currentVersion}");

            Puts("[RustRoyale] Checking remote plugin for updates...");

            lang = Interface.Oxide.GetLibrary<Lang>();

            webrequest.Enqueue(PluginUpdateUrl, null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response))
                {
                    PrintWarning($"[RustRoyale] Failed to fetch plugin source from GitHub. Code: {code}");
                    return;
                }

                string latestVersion = ExtractVersionFromSource(response);
                if (string.IsNullOrEmpty(latestVersion))
                {
                    PrintWarning("[RustRoyale] Could not extract version from GitHub source.");
                    return;
                }

                LatestPluginVersion = latestVersion;

                VersionNumber current = ParseVersion(currentVersion);
                VersionNumber latest = ParseVersion(latestVersion);

                Puts($"[RustRoyale] Latest available version: {latest}");

                if (latest <= current)
                {
                    Puts("[RustRoyale] Plugin is up to date.");
                    return;
                }

                PrintWarning($"[RustRoyale] A newer version is available: {latest}");

                if (!Configuration.EnableAutoUpdate)
                {
                    PrintWarning("[RustRoyale] Auto-update is disabled. Skipping update.");
                    return;
                }

                Puts("[RustRoyale] Auto-update enabled. Downloading and updating plugin...");

                try
                {
                    string path = Path.Combine(Interface.Oxide.PluginDirectory, Name + ".cs");
                    File.WriteAllText(path, response);
                    Puts("[RustRoyale] Plugin updated successfully, reloading plugin...");
                    timer.Once(2f, () => Server.Command($"oxide.reload {Name}"));
                }
                catch (Exception ex)
                {
                    PrintError($"[RustRoyale] Failed to write plugin file: {ex.Message}");
                }
            }, this);

            foreach (var player in BasePlayer.activePlayerList)
            {
                OnPlayerInit(player);
            }

            if (isTournamentRunning)
            {
                if (DateTime.UtcNow >= tournamentEndTime)
                {
                    Puts("[Guard] Init found a past-due tournament; ending now.");
                    EndTournament();
                }
                else
                {
                    var remaining = (float)(tournamentEndTime - DateTime.UtcNow).TotalSeconds;
                    if (remaining > 0f)
                    {
                        tournamentDurationTimer?.Destroy();
                        tournamentDurationTimer = timer.Once(remaining, () =>
                        {
                            if (isTournamentRunning) EndTournament();
                        });
                    }
                }
            }
        }

    #endregion
    #region Schedule Tournament
        private readonly Dictionary<ulong, PlayerStats> playerStats = new Dictionary<ulong, PlayerStats>();
        private readonly HashSet<ulong> participants = new HashSet<ulong>();
        private readonly HashSet<ulong> openTournamentPlayers = new HashSet<ulong>();
        private readonly HashSet<ulong> inactiveParticipants = new HashSet<ulong>();
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
                var tz = GetTimezone();

                DateTime utcNow   = DateTime.UtcNow;
                DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
                Puts($"[Debug] The server thinks it's currently {utcNow:yyyy-MM-dd HH:mm:ss} UTC.");
                Puts($"[Debug] localNow is {localNow:yyyy-MM-dd HH:mm:ss} (Timezone={Configuration.Timezone}).");

                DayOfWeek startDay = Enum.Parse<DayOfWeek>(Configuration.StartDay, true);
                DateTime nextLocalStart = localNow.Date
                    .AddDays((7 + (int)startDay - (int)localNow.DayOfWeek) % 7)
                    .AddHours(Configuration.StartHour)
                    .AddMinutes(Configuration.StartMinute);
                if (nextLocalStart <= localNow)
                    nextLocalStart = nextLocalStart.AddDays(7);

                tournamentStartTime = TimeZoneInfo.ConvertTimeToUtc(nextLocalStart, tz);
                LogEvent($"Tournament scheduled to start on {nextLocalStart:yyyy-MM-dd HH:mm:ss} ({Configuration.Timezone}).");

                int activeCount = participantsData.Keys.Count(id => !inactiveParticipants.Contains(id));
                var tokens = new Dictionary<string, string>
                {
                    { "StartTime", nextLocalStart.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "Timezone",  Configuration.Timezone },
                    { "Duration",  Configuration.DurationHours.ToString() },
                    { "ActiveCount", activeCount.ToString() }
                };

                BroadcastLocalized("ScheduleMessage", tokens);
                if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
                {
                    SendDiscordMessage(
                        Lang("ScheduleMessage", null, tokens, Configuration.DefaultLanguage)
                    );
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
            var tz = GetTimezone();

            DateTime utcNow   = DateTime.UtcNow;
            DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);

            DateTime nextLocal = localNow.Date.AddHours(Configuration.StartHour);
            if (nextLocal <= localNow)
                nextLocal = nextLocal.AddDays(1);

            DateTime nextReminder = TimeZoneInfo.ConvertTimeToUtc(nextLocal, tz);
            LogEvent($"Daily reminder scheduled for {nextLocal:yyyy-MM-dd HH:mm:ss} ({Configuration.Timezone}) → {nextReminder:yyyy-MM-dd HH:mm:ss} UTC.");

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
                var tokens = new Dictionary<string, string>
                {
                    { "Time", formattedTime }
                };

                Puts($"[Debug] Sending tournament countdown message: {formattedTime}");

                string globalMessage = Lang("TournamentCountdown", null, tokens);

                BroadcastLocalized("TournamentCountdown", tokens);

                Puts($"[Debug] Tournament countdown message sent to global chat: {globalMessage}");

                if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
                {
                    SendDiscordMessage(
                        Lang("TournamentCountdown", null, tokens, Configuration.DefaultLanguage)
                    );
                }
            }
        }

        private void NotifyTournamentStartImminent()
        {
            string timeRemaining = FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow);

            var tokens = new Dictionary<string, string>
            {
                { "Time", timeRemaining }
            };

            Notify("TournamentAboutToStart", null, extraPlaceholders: tokens);

            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                var defaultLang = Configuration.DefaultLanguage;
                string discordMessage = Lang("TournamentAboutToStartDiscord", null, tokens, defaultLang);
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
        private int GetEffectiveScore(string actionCode)
        {
            if (!Configuration.ScoreRules.TryGetValue(actionCode, out int basePoints))
                return 0;

            return basePoints;
        }
        
        private bool IsActionCodeDisabled(string actionCode)
        {
            return Configuration.ScoreRules.TryGetValue(actionCode, out int value) && value == 0;
        }

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

            if (latestFile == null)
                return;

            var lines = File.ReadAllLines(latestFile);

            if (lines.Any(l => l.Contains("Tournament ended successfully.")))
            {
                Puts($"[RustRoyale] Found latest tournament file {Path.GetFileName(latestFile)} already marked as completed.");
                
                isTournamentRunning = false;
                currentTournamentFile = null;
                return;
            }

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
            tournamentEndTime = startUtc.AddHours(Configuration.DurationHours);
            isTournamentRunning = true;

            if (DateTime.UtcNow >= tournamentEndTime)
            {
                EndTournament();
            }
            else
            {
                ScheduleTournamentEnd();

                TimeSpan remaining = tournamentEndTime - DateTime.UtcNow;

                var tokens = new Dictionary<string, string>
                {
                    { "TimeRemaining", FormatTimeRemaining(remaining) },
                    { "Duration", Configuration.DurationHours.ToString() }
                };

                BroadcastLocalized("ResumeTournament", tokens);
                if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
                {
                    SendDiscordMessage(
                        Lang("ResumeTournament", null, tokens, Configuration.DefaultLanguage)
                    );
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

                string message = Lang("TournamentAlreadyRunning", null, new Dictionary<string, string>
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
            npcKillCounts.Clear();
            animalKillCounts.Clear();

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

                var startTokens = new Dictionary<string,string> {
                    { "TimeRemaining", FormatTimeRemaining(tournamentEndTime - DateTime.UtcNow) },
                    { "Duration",       Configuration.DurationHours.ToString()         }
                };
                BroadcastLocalized("StartTournament", startTokens);
                if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
                {
                    SendDiscordMessage(
                        Lang("StartTournament", null, startTokens, Configuration.DefaultLanguage)
                    );
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
                    SaveTeamLeaderNames();
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
                
                currentTournamentFile = null;

                string resultsMessage = "Leaderboard:\n";
                if (sortedParticipants.Any())
                {
                    resultsMessage += string.Join("\n", sortedParticipants
                        .Select((p, idx) => $"{idx + 1}. {p.Name} - {p.Score} Points"));
                }
                else
                {
                    resultsMessage += Lang("NoScoresInTournament");
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
                globalMessage.AppendLine(Lang("EndTournament"));
                globalMessage.AppendLine();
                globalMessage.AppendLine(resultsMessage);

                if (topClans.Any())
                {
                    globalMessage.AppendLine();
                    globalMessage.AppendLine(Lang("TopClansHeader"));
                    globalMessage.AppendLine(string.Join("\n", topClans
                        .Select((kv, idx) =>
                        {
                            string name = groupNames.ContainsKey(kv.Key) ? groupNames[kv.Key] : kv.Key;
                            return $"{idx + 1}. {name} ({kv.Value} pts)";
                        })));
                }

                var endTokens = new Dictionary<string, string>();

                BroadcastLocalized("EndTournament", endTokens);
                
                foreach (var p in participantsData.Values)
                    p.Score = 0;
                SaveParticipantsData();

                if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
                {
                    SendDiscordMessage(globalMessage.ToString());
                }

                Puts($"[Debug] Notifications sent for tournament end:\n{globalMessage}");
                Puts($"[Debug] Tournament successfully ended. Total participants: {sortedParticipants.Count}");

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
            if (!initialized)
                return;
            
            if (participantsData.TryGetValue(player.userID, out var participant) && participant.Name == "Unknown")
            {
                participant.Name = player.displayName;
                SaveParticipantsData();
                Puts($"Updated participant data: {player.userID} now has name '{player.displayName}'.");
            }
            
            if (!hasRebuiltCaps)
            {
                RebuildKillCapsFromHistory();
                hasRebuiltCaps = true;
            }

            if (!playerLanguages.ContainsKey(player.userID))
            {
                var raw = player.net?.connection?.info?.GetString("global.language", Configuration.DefaultLanguage);
                if (!string.IsNullOrEmpty(raw))
                {
                    var code = raw.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
                    if (Translations.ContainsKey(code))
                    {
                        playerLanguages[player.userID] = code;
                        SavePlayerLanguages();
                        Puts($"[Debug] Detected language '{code}' for {player.displayName}.");
                    }
                    else
                    {
                        Puts($"[Debug] Client requested unsupported language '{raw}', falling back to '{Configuration.DefaultLanguage}'.");
                    }
                }
            }

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
                    SaveTeamLeaderNames();
                    Puts($"[Debug] Cached leader name '{player.displayName}' for team {player.currentTeam}");
                }
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
                SendPlayerMessage(player, Lang("AutoEnrolled", player));
                Puts($"[Debug] Automatically enrolled player: {player.displayName} ({player.userID})");
            }
        }
        
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;

            timer.Once(2f, () =>
            {
                if (player == null || !player.IsConnected)
                    return;

                OnPlayerInit(player);

                if (Configuration.ShowWelcomeUI && !welcomeOptOut.Contains(player.userID))
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
    #endregion
    #region DeathHandlers
        private readonly ConcurrentDictionary<ulong, DateTime> recentDeaths = new ConcurrentDictionary<ulong, DateTime>();
        private readonly Dictionary<ulong, (BasePlayer Attacker, float TimeStamp)> lastDamageRecords = new Dictionary<ulong, (BasePlayer, float)>();
        private readonly Dictionary<ulong, BasePlayer> lastEntityDamageRecords = new Dictionary<ulong, BasePlayer>();
        private readonly Dictionary<ulong, (string Prefab, float TimeStamp)> lastNpcOrHeliDamage = new Dictionary<ulong, (string Prefab, float)>();
        private const float HeliAttributionWindow = 8f;
        private const int RecentDeathWindowSeconds = 5;

        private static bool IsHeliPrefab(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            var p = prefab.ToLowerInvariant();
            return p.Contains("patrolhelicopter") || p.Contains("ch47");
        }

        private static string GetAnyInitiatorPrefab(HitInfo info)
        {
            if (info == null) return string.Empty;
            var init = info.Initiator as BaseCombatEntity;
            var prefab = init?.ShortPrefabName ?? string.Empty;
            if (string.IsNullOrEmpty(prefab))
                prefab = info.WeaponPrefab?.ShortPrefabName ?? string.Empty;
            if (!string.IsNullOrEmpty(prefab) && !IsHeliPrefab(prefab) && init != null)
            {
                var parent = init.GetParentEntity() as BaseCombatEntity;
                var pp = parent?.ShortPrefabName ?? string.Empty;
                if (!string.IsNullOrEmpty(pp))
                    prefab = pp;
            }
            return prefab ?? string.Empty;
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is BasePlayer victim && info != null)
            {
                if (info.InitiatorPlayer != null)
                {
                    lastDamageRecords[victim.userID] = (info.InitiatorPlayer, UnityEngine.Time.realtimeSinceStartup);
                }
                else if (info.Initiator != null && info.Initiator.net != null)
                {
                    var ownerId = info.Initiator.OwnerID;
                    if (ownerId != 0)
                    {
                        var owner = BasePlayer.FindByID(ownerId);
                        if (owner != null)
                            lastDamageRecords[victim.userID] = (owner, UnityEngine.Time.realtimeSinceStartup);
                    }
                }

                var anyPrefab = GetAnyInitiatorPrefab(info);
                if (IsHeliPrefab(anyPrefab))
                {
                    lastNpcOrHeliDamage[victim.userID] = (anyPrefab, UnityEngine.Time.realtimeSinceStartup);
                    Puts($"[Debug] Noted heli damage to {victim.displayName} by '{anyPrefab}' at t={UnityEngine.Time.realtimeSinceStartup:0.00}");
                }
            }

            if (entity?.net != null)
            {
                BasePlayer killer = info?.InitiatorPlayer;

                if (killer == null && info?.Initiator != null)
                {
                    var ownerId = info.Initiator.OwnerID;
                    if (ownerId != 0)
                        killer = BasePlayer.FindByID(ownerId);
                }

                if (killer != null)
                    lastEntityDamageRecords[entity.net.ID.Value] = killer;
            }
        }
        
        private static readonly Dictionary<string, string> AnimalNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "wolf", "Wolf" },
            { "boar", "Boar" },
            { "bear", "Bear" },
            { "polar_bear", "Polar Bear" },
            { "polarbear", "Polar Bear" },
            { "stag", "Deer" },
            { "deer", "Deer" },
            { "chicken", "Chicken" },
            { "horse", "Horse" },
            { "crocodile", "Crocodile" },
            { "panther", "Panther" },
            { "tiger", "Tiger" },
            { "snake", "Snake" }
        };

        private static readonly Dictionary<string, string> NpcNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "scientist",           "Scientist" },
            { "scientistnpc",        "Scientist" },
            { "scientistnpcnew",     "Scientist" },
            { "scientist_full",      "Scientist" },
            { "scientist_heavy",     "Heavy Scientist" },
            { "heavy_scientist",     "Heavy Scientist" },
            { "scientistmarine",     "Scientist" },
            { "scientist_arena",     "Scientist" },
            { "npcmurderer",         "Murderer" },
            { "scarecrow",           "Scarecrow" },
            { "npc_tunneldweller",   "Tunnel Dweller" },
            { "tunneldweller",       "Tunnel Dweller" },
            { "underwaterdweller",   "Underwater Dweller" },
            { "bandit_guard",        "Bandit Guard" },
            { "npc_bandit_guard",    "Bandit Guard" },
            { "npc_player",          "NPC Player" }
        };

        private static readonly Dictionary<string, string> TrapNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "autoturret_deployed",        "Auto Turret" },
            { "guntrap",                    "Gun Trap" },
            { "flameturret.deployed",       "Flame Turret" },
            { "beartrap",                   "Bear Trap" },
            { "wooden_floor_spike_cluster", "Floor Spike" },
            { "landmine",                   "Landmine" },
            { "barricade",                  "Barricade" },
            { "sentry.bandit.static",       "Sentry Gun" }
        };

        private static readonly Dictionary<string, string> EnvironmentalDeathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "fall",               "Fall Damage" },
            { "drowned",            "Drowning" },
            { "burn",               "Fire Damage" },
            { "cold",               "Hypothermia" },
            { "heat",               "Heatstroke" },
            { "radiation",          "Radiation Sickness" },
            { "bleeding",           "Bleeding Out" },
            { "coldexposure",       "Hypothermia (Exposure)" },
            { "radiationexposure",  "Radiation Exposure" },
            { "suicide",            "Suicide" },
            { "poison",             "Poisoning" }
        };

        private string GetFriendlyNpcName(string prefab, BasePlayer viewer = null)
        {
            if (string.IsNullOrEmpty(prefab))
                return Lang("UnknownEntity", viewer);

            string lower = prefab.ToLowerInvariant();
            foreach (var kvp in NpcNameMap)
            {
                if (lower == kvp.Key || lower.StartsWith(kvp.Key + ".") || lower.StartsWith(kvp.Key + "_"))
                {
                    string npcKey = kvp.Value.Replace(" ", "").Replace("-", "").Replace("_", "");
                    string langKey = $"Npc_{npcKey}";
                    string localized = Lang(langKey, viewer);
                    return !string.IsNullOrEmpty(localized) && localized != langKey ? localized : kvp.Value;
                }
            }
            return prefab;
        }

        private string GetFriendlyTrapName(string prefab, BasePlayer viewer = null)
        {
            if (string.IsNullOrEmpty(prefab))
                return Lang("UnknownEntity", viewer);

            string lower = prefab.ToLowerInvariant();
            foreach (var kvp in TrapNameMap)
            {
                if (lower == kvp.Key || lower.StartsWith(kvp.Key + ".") || lower.StartsWith(kvp.Key + "_"))
                {
                    string trapKey = kvp.Value.Replace(" ", "").Replace("-", "").Replace("_", "");
                    string langKey = $"Trap_{trapKey}";
                    var localized = Lang(langKey, viewer);
                    return !string.IsNullOrEmpty(localized) && localized != langKey ? localized : kvp.Value;
                }
            }
            return prefab;
        }

        private string GetFriendlyAnimalName(string prefab, BasePlayer viewer = null)
        {
            if (string.IsNullOrEmpty(prefab))
                return Lang("UnknownEntity", viewer);

            string lower = prefab.ToLowerInvariant();
            foreach (var kvp in AnimalNameMap)
            {
                if (lower == kvp.Key || lower.StartsWith(kvp.Key + ".") || lower.StartsWith(kvp.Key + "_") || lower.StartsWith(kvp.Key + "2"))
                {
                    var key = $"Animal_{kvp.Value.Replace(" ", "")}";
                    var localized = Lang(key, viewer);
                    return !string.IsNullOrEmpty(localized) && localized != key ? localized : kvp.Value;
                }
            }
            return prefab;
        }

        private string GetFriendlyEnvironmentalCause(string causeKey, BasePlayer viewer = null)
        {
            if (string.IsNullOrEmpty(causeKey))
                return Lang("UnknownEntity", viewer);

            string lower = causeKey.ToLowerInvariant();
            foreach (var kvp in EnvironmentalDeathMap)
            {
                if (lower == kvp.Key || lower.StartsWith(kvp.Key))
                {
                    string keyName = kvp.Value.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
                    string langKey = $"Env_{keyName}";
                    string localized = Lang(langKey, viewer);
                    return !string.IsNullOrEmpty(localized) && localized != langKey ? localized : kvp.Value;
                }
            }
            return Lang("UnknownEntity", viewer);
        }

        private bool IsAnimalKill(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            var lower = prefabName.ToLowerInvariant();
            bool match = AnimalNameMap.Keys.Any(animal =>
                lower == animal ||
                lower.StartsWith(animal + ".") ||
                lower.StartsWith(animal + "_") ||
                lower.StartsWith(animal + "2"));
            if (!match)
                Puts($"[Debug] Skipped prefab '{lower}' in IsAnimalKill()");
            return match;
        }

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            if (!isTournamentRunning || victim == null)
                return;
            if (IsRecentDeath(victim))
                return;
            var attacker = ResolveAttacker(victim, info);
            var entity = info?.Initiator as BaseCombatEntity;
            if (HandleHeliOrBradleyKill(victim, entity, info))      return;
            if (HandleSelfInflicted(victim, attacker, info))        return;
            if (HandlePlayerKill(victim, attacker, info))           return;
            if (HandleTrapKill(victim, entity, info))               return;
            if (HandleNpcKilledByPlayer(victim, attacker, info))    return;
            if (HandleNpcKilledByTrap(victim, entity, info))        return;
            if (HandleNpcOrUnownedEntityKill(victim, attacker, entity, info)) return;
            if (TryScoreHeliFallback(victim, info))                 return;
            Puts($"[Debug] ❌ No handler matched for {victim.displayName}'s death. Entity: {entity?.ShortPrefabName ?? "unknown"}");
            lastDamageRecords.Remove(victim.userID);
        }

        private bool IsRecentDeath(BasePlayer victim)
        {
            if (recentDeaths.TryGetValue(victim.userID, out var lastDeathTime) &&
                (DateTime.UtcNow - lastDeathTime).TotalSeconds < RecentDeathWindowSeconds)
            {
                Puts($"[Debug] Ignoring duplicate death event for {victim.displayName}. Last death: {lastDeathTime}.");
                return true;
            }
            recentDeaths[victim.userID] = DateTime.UtcNow;
            return false;
        }

        private BasePlayer ResolveAttacker(BasePlayer victim, HitInfo info)
        {
            var attacker = info?.InitiatorPlayer;
            if (attacker == null
                && lastDamageRecords.TryGetValue(victim.userID, out var record)
                && UnityEngine.Time.realtimeSinceStartup - record.TimeStamp <= 30f)
            {
                attacker = record.Attacker;
                Puts($"[Debug] Overriding final attacker with last real attacker: {attacker?.displayName}");
            }
            return attacker;
        }

        private bool HandleHeliOrBradleyKill(BasePlayer victim, BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || !participants.Contains(victim.userID))
                return false;
            string prefab = (entity.ShortPrefabName ?? string.Empty).ToLowerInvariant();
            bool isHeli  = prefab.Contains("heli");
            bool isBrad  = prefab.Contains("bradley");
            if (!isHeli && !isBrad)
            {
                var parent = entity.GetParentEntity() as BaseCombatEntity;
                if (parent != null)
                {
                    var p = (parent.ShortPrefabName ?? string.Empty).ToLowerInvariant();
                    isHeli = p.Contains("heli");
                    isBrad = p.Contains("bradley");
                }
            }
            if (!isHeli && !isBrad) 
                return false;
            if (IsActionCodeDisabled("BRUH")) 
                return false;
            string type = isBrad ? "Bradley" : "Helicopter";
            UpdatePlayerScore(
                victim.userID,
                "BRUH",
                $"defeated by a {type}",
                victim, info,
                entityName: type,
                rawEntityType: "npc",
                rawEntityPrefab: prefab
            );
            Puts($"[Debug] {victim.displayName} lost {GetEffectiveScore("BRUH")} point(s) for death to a {type}.");
            return true;
        }

        private bool HandleNpcOrUnownedEntityKill(BasePlayer victim, BasePlayer attacker, BaseCombatEntity entity, HitInfo info)
        {
            ulong ownerId = entity?.OwnerID ?? 0;
            bool isNpcKill = attacker != null && attacker.IsNpc;
            bool isUnownedEntity = entity != null && ownerId == 0 && (attacker == null || attacker.IsNpc);
            if (!isNpcKill && !isUnownedEntity)
                return false;
            string attackerName = attacker?.ShortPrefabName ?? entity?.ShortPrefabName ?? "Unknown";
            attackerName = GetFriendlyNpcName(attackerName, victim);
            if (!participants.Contains(victim.userID) || IsActionCodeDisabled("BRUH"))
                return false;
            UpdatePlayerScore(victim.userID, "BRUH", $"being defeated by {attackerName}", victim, info, entityName: attackerName, rawEntityType: "npc", rawEntityPrefab: (attacker != null ? attacker.ShortPrefabName : entity?.ShortPrefabName));
            int finalPoints = GetEffectiveScore("BRUH");
            Puts($"[Debug] {victim.displayName} lost {finalPoints} point(s) for being defeated by {attackerName}.");
            return true;
        }

        private bool HandlePlayerKill(BasePlayer victim, BasePlayer attacker, HitInfo info)
        {
            if (attacker == null 
                || !participants.Contains(attacker.userID) 
                || !participants.Contains(victim.userID))
                return false;
            participantsData.TryAdd(victim.userID,   new PlayerStats(victim.userID)  { Name = victim.displayName });
            participantsData.TryAdd(attacker.userID, new PlayerStats(attacker.userID){ Name = attacker.displayName });
            bool updated = false;
            if (!IsActionCodeDisabled("DEAD"))
            {
                UpdatePlayerScore(
                    victim.userID,
                    "DEAD",
                    $"being killed by {attacker.displayName}",
                    victim,
                    info,
                    attackerName: attacker.displayName
                );
                updated = true;
            }
            if (!IsActionCodeDisabled("KILL"))
            {
                string weapon = info?.Weapon?.ShortPrefabName ?? "their weapon";
                UpdatePlayerScore(
                    attacker.userID,
                    "KILL",
                    $"eliminating {victim.displayName}",
                    victim,
                    info,
                    attackerName: attacker.displayName,
                    entityName: weapon,
                    reverseMessage: true
                );
                updated = true;
            }
            if (updated)
            {
                int deadPts = GetEffectiveScore("DEAD");
                int killPts = GetEffectiveScore("KILL");
                Puts($"[Debug] {attacker.displayName} killed {victim.displayName} (KILL +{killPts}, DEAD -{deadPts})");
            }
            return true;
        }

        private bool HandleTrapKill(BasePlayer victim, BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || !participants.Contains(victim.userID))
                return false;
            string prefab = (entity.ShortPrefabName ?? "").ToLowerInvariant();
            if (!TrapNameMap.ContainsKey(prefab))
                return false;
            string entityName = entity.ShortPrefabName;
            string friendlyName = GetFriendlyTrapName(entityName, victim);
            ulong ownerId = entity.OwnerID;
            string ownerName = ownerId != 0 ? GetPlayerName(ownerId) : Lang("UnknownEntity", victim);

            if (ownerId == victim.userID)
            {
                if (!IsActionCodeDisabled("JOKE"))
                {
                    UpdatePlayerScore(victim.userID, "JOKE",
                      $"self-inflicted death with {friendlyName}", victim, info,
                      entityName: friendlyName,
                      rawEntityType: "trap",
                      rawEntityPrefab: entity.ShortPrefabName);
                    Puts($"[Debug] {victim.displayName} lost {GetEffectiveScore("JOKE")} to their own {friendlyName}.");
                    return true;
                }
            }
            if (ownerId != 0 && participants.Contains(ownerId))
            {
                bool scored = false;
                if (!IsActionCodeDisabled("DEAD"))
                {
                    UpdatePlayerScore(victim.userID, "DEAD", $"being killed by {friendlyName} owned by {ownerName}", victim, info, attackerName: ownerName, entityName: friendlyName, rawEntityType: "trap", rawEntityPrefab: entity.ShortPrefabName);
                    scored = true;
                }
                if (!IsActionCodeDisabled("KILL"))
                {
                    UpdatePlayerScore(ownerId, "KILL", $"eliminating {victim.displayName} with {friendlyName}", victim, info, attackerName: ownerName, entityName: friendlyName, reverseMessage: true, rawEntityType: "trap", rawEntityPrefab: entity.ShortPrefabName);
                    scored = true;
                }
                if (scored)
                {
                    int killPts = GetEffectiveScore("KILL");
                    int deadPts = GetEffectiveScore("DEAD");
                    Puts($"[Debug] {ownerName} earned {killPts}, {victim.displayName} lost {deadPts} via {friendlyName}");
                }
                return true;
            }
            if (!IsActionCodeDisabled("JOKE"))
            {
                UpdatePlayerScore(victim.userID, "JOKE",
                  $"death caused by an unowned {friendlyName}",
                  victim, info,
                  entityName: friendlyName,
                  rawEntityType: "trap",
                  rawEntityPrefab: entity.ShortPrefabName);

                int points = GetEffectiveScore("JOKE");
                Puts($"[Debug] {victim.displayName} lost {points} point(s) to an unowned {friendlyName}.");
                return true;
            }
            return false;
        }

        private bool HandleNpcKilledByPlayer(BasePlayer victim, BasePlayer attacker, HitInfo info)
        {
            if (!victim.IsNpc || attacker == null || !participants.Contains(attacker.userID))
                return false;
            if (IsActionCodeDisabled("NPC"))
                return false;
            string npcName = GetFriendlyNpcName(victim.ShortPrefabName, attacker);
            if (HasReachedKillCap(npcKillCounts, attacker.userID, Configuration.NpcKillCap, "NPC", attacker))
                return false;
            UpdatePlayerScore(
                attacker.userID,
                "NPC",
                $"eliminating an NPC ({npcName})",
                victim, info,
                entityName: npcName,
                rawEntityType: "npc",
                rawEntityPrefab: victim.ShortPrefabName
            );
            int finalPoints = GetEffectiveScore("NPC");
            Puts($"[Debug] {attacker.displayName} earned {finalPoints} pt(s) for killing NPC {npcName}");
            return true;
        }

        private bool HandleNpcKilledByTrap(BasePlayer victim, BaseCombatEntity entity, HitInfo info)
        {
            if (!victim.IsNpc || entity == null)
                return false;
            ulong ownerId = entity.OwnerID;
            if (ownerId == 0 || !participants.Contains(ownerId))
                return false;
            if (IsActionCodeDisabled("NPC"))
                return false;
            if (HasReachedKillCap(npcKillCounts, ownerId, Configuration.NpcKillCap, "NPC", BasePlayer.FindByID(ownerId)))
                return false;
            var owner = BasePlayer.FindByID(ownerId);
            string trapName = GetFriendlyTrapName(entity.ShortPrefabName ?? "Unknown", owner);
            string npcName = GetFriendlyNpcName(victim.ShortPrefabName ?? "Unknown", owner);
            string ownerName = GetPlayerName(ownerId);

            UpdatePlayerScore(ownerId, "NPC", $"eliminating an NPC ({npcName}) with {trapName}", victim, info, attackerName: ownerName, entityName: npcName, rawEntityType: "npc", rawEntityPrefab: victim.ShortPrefabName);
            int finalPoints = GetEffectiveScore("NPC");
            Puts($"[Debug] {ownerName} earned {finalPoints} pt(s) for killing NPC {npcName} with {trapName}");
            return true;
        }

        private bool HandleSelfInflicted(BasePlayer victim, BasePlayer attacker, HitInfo info)
        {
            if (!participants.Contains(victim.userID) || IsActionCodeDisabled("JOKE"))
                return false;
            var causeType = info?.damageTypes?.GetMajorityDamageType();
            string causeKey = causeType?.ToString().ToLowerInvariant() ?? string.Empty;
            bool noAttacker = attacker == null || attacker == victim;
            bool isEnv = string.IsNullOrEmpty(causeKey) || EnvironmentalDeathMap.ContainsKey(causeKey);
            bool isSuicide = causeType == DamageType.Suicide;
            if (isSuicide || isEnv)
            {
                attacker = null;
            }
            if (noAttacker && (isEnv || isSuicide))
            {
                string friendlyCause = isSuicide ? Lang("Env_Suicide", victim) : GetFriendlyEnvironmentalCause(causeKey, victim);
                UpdatePlayerScore(
                    victim.userID,
                    "JOKE",
                    $"self-inflicted death ({friendlyCause})",
                    victim,
                    info,
                    entityName: friendlyCause
                );

                Puts($"[Debug] {victim.displayName} died from {friendlyCause}. Lost {GetEffectiveScore("JOKE")} point(s).");
                return true;
            }
            return false;
        }

        private bool TryScoreHeliFallback(BasePlayer victim, HitInfo info)
        {
            if (victim == null || !participants.Contains(victim.userID) || IsActionCodeDisabled("BRUH"))
                return false;
            var majority = info?.damageTypes?.GetMajorityDamageType();
            string causeKey = majority?.ToString().ToLowerInvariant() ?? string.Empty;
            bool noAttacker = (info?.InitiatorPlayer == null && info?.Initiator == null);
            bool looksEnvOrUnknown = string.IsNullOrEmpty(causeKey) || EnvironmentalDeathMap.ContainsKey(causeKey);
            if (!noAttacker || !looksEnvOrUnknown)
                return false;
            if (lastNpcOrHeliDamage.TryGetValue(victim.userID, out var rec))
            {
                float age = UnityEngine.Time.realtimeSinceStartup - rec.TimeStamp;
                if (age <= HeliAttributionWindow && IsHeliPrefab(rec.Prefab))
                {
                    string friendly = rec.Prefab.IndexOf("ch47", StringComparison.OrdinalIgnoreCase) >= 0 ? "CH-47" : "Patrol Helicopter";
                    UpdatePlayerScore(victim.userID, "BRUH", $"defeated by the {friendly}", victim, info, entityName: friendly);
                    Puts($"[Debug] {victim.displayName} lost {GetEffectiveScore("BRUH")} pt(s) to {friendly} via environmental final blow (fallback, {age:0.0}s).");
                    return true;
                }
            }
            return false;
        }
    #endregion

    #region OnEntityDeath
        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (!isTournamentRunning || entity == null || string.IsNullOrEmpty(entity.ShortPrefabName))
                return;

            if (HandleDeathByBradleyOrHelicopter(entity, info)) return;
            if (HandleLongDistanceAnimalKill(entity, info)) return;

            if (!IsAnimalKill(entity.ShortPrefabName) && !entity.ShortPrefabName.Contains("scientist"))
                return;

            Puts($"[Debug] ❌ No OnEntityDeath handler matched for entity: {entity.ShortPrefabName}");
        }

        private bool HandleDeathByBradleyOrHelicopter(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || !isTournamentRunning) return false;

            var prefab = (entity.ShortPrefabName ?? string.Empty).ToLowerInvariant();

            bool isBradley = prefab == "bradleyapc";
            bool isPatrol  = prefab == "patrolhelicopter";
            bool isCh47    = prefab == "ch47.entity" || prefab == "ch47scientists.entity";

            if (!isBradley && !isPatrol && !isCh47)
                return false;

            BasePlayer killer = info?.InitiatorPlayer;
            if (killer == null && entity.net != null)
                lastEntityDamageRecords.TryGetValue(entity.net.ID.Value, out killer);

            if (killer == null || !participants.Contains(killer.userID))
                return false;
            if (IsActionCodeDisabled("ENT"))
                return false;

            string type = isBradley ? "Bradley"
                        : isCh47    ? "Cargo Helicopter"
                        : "Patrol Helicopter";

            UpdatePlayerScore(
                killer.userID,
                "ENT",
                $"destroying a {type}",
                null, info,
                entityName: type,
                rawEntityType: "npc",
                rawEntityPrefab: prefab
            );

            Puts($"[Debug] {killer.displayName} earned {GetEffectiveScore("ENT")} point(s) for downing a {type}.");

            if (entity.net != null)
                lastEntityDamageRecords.Remove(entity.net.ID.Value);

            return true;

        }
        
        private bool HandleLongDistanceAnimalKill(BaseCombatEntity entity, HitInfo info)
        {
            if (!IsAnimalKill(entity.ShortPrefabName))
                return false;

            var killer = info?.InitiatorPlayer;
            if (killer == null || !participants.Contains(killer.userID))
                return false;

            bool isProjectile =
                (info != null && (info.IsProjectile()
                                  || info.Weapon is BaseProjectile));

            if (!isProjectile)
            {
                Puts($"[Debug] Ignored long-range animal kill by {killer.displayName} — not a projectile weapon.");
                return false;
            }

            float distance = Vector3.Distance(killer.transform.position, entity.transform.position);
            if (distance <= Configuration.AnimalKillDistance)
                return false;

            if (IsActionCodeDisabled("WHY"))
                return false;

            if (HasReachedKillCap(animalKillCounts, killer.userID, Configuration.AnimalKillCap, "animal", killer))
                return false;

            string animalName = GetFriendlyAnimalName(entity.ShortPrefabName, killer);

            UpdatePlayerScore(killer.userID, "WHY", $"killing an animal ({animalName}) from {distance:F1} meters away", null, info, entityName: animalName, distance: distance, rawEntityType: "animal", rawEntityPrefab: entity.ShortPrefabName);

            int finalPoints = GetEffectiveScore("WHY");
            Puts($"[Debug] Awarded {finalPoints} pt(s) to {killer.displayName} for {animalName} kill at {distance:F1} m");

            return true;
        }
    #endregion
    #region Kill Cap
        private class KillRecord
        {
            public ulong AttackerId { get; set; }
            public string ActionCode { get; set; }
        }

        private bool HasReachedKillCap(Dictionary<ulong, int> tracker, ulong userId, int cap, string label, BasePlayer player = null)
        {
            if (cap <= 0) return false;

            tracker.TryGetValue(userId, out int count);
            if (count >= cap)
            {
                Puts($"[Debug] {(player?.displayName ?? userId.ToString())} reached {label} cap ({cap}).");

                string template = Lang("MaxKillsReached", player);
                string rawMsg   = string.Format(template, label);

                if (player != null)
                    SendPlayerMessage(player, rawMsg);

                return true;
            }

            tracker[userId] = count + 1;
            return false;
        }

        private void RebuildKillCapsFromHistory()
        {
            npcKillCounts.Clear();
            animalKillCounts.Clear();
            
            if (!Directory.Exists(DataDirectory))
                return;

            var filePaths = Directory
                .GetFiles(DataDirectory, "Tournament_*.data")
                .OrderByDescending(f => f)
                .Take(1);

            foreach (var path in filePaths)
            {
                Puts($"[RustRoyale] Attempting to rebuild kill caps from: {Path.GetFileName(path)}");
                try
                {
                    var lines = File.ReadAllLines(path);
                    foreach (var line in lines)
                    {
                        if (!line.Contains("[Event]") || !line.Contains(" received "))
                            continue;

                        var partsAfterEvent = line.Split(new[] { "[Event]" }, StringSplitOptions.None);
                        if (partsAfterEvent.Length < 2)
                            continue;

                        string afterEvent = partsAfterEvent[1].Trim();

                        var partsReceived = afterEvent.Split(new[] { " received " }, StringSplitOptions.None);
                        if (partsReceived.Length < 2)
                            continue;

                        string nameToken = partsReceived[0].Trim();
                        string restOfPayload = partsReceived[1].Trim();

                        ulong attackerId = 0UL;
                        foreach (var kvp in participantsData)
                        {
                            if (kvp.Value.Name.Equals(nameToken, StringComparison.OrdinalIgnoreCase))
                            {
                                attackerId = kvp.Key;
                                break;
                            }
                        }

                        if (attackerId == 0UL)
                            continue;

                        if (restOfPayload.Contains("eliminating "))
                        {
                            npcKillCounts[attackerId] = npcKillCounts.GetValueOrDefault(attackerId) + 1;
                        }
                        else if (restOfPayload.Contains("killing a "))
                        {
                            animalKillCounts[attackerId] = animalKillCounts.GetValueOrDefault(attackerId) + 1;
                        }
                    }

                    Puts($"[RustRoyale] Restored kill caps from {Path.GetFileName(path)}: " +
                         $"{npcKillCounts.Count} NPC killers, {animalKillCounts.Count} animal killers.");
                }
                catch (Exception ex)
                {
                    PrintError($"[RustRoyale] Failed to rebuild kill caps from {path}: {ex.Message}");
                }
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

                lock (participantsDataLock)
                {
                    if (participantsData == null || participantsData.Count == 0)
                    {
                        PrintWarning("[SaveParticipantsData] Aborting save: No participants to write.");
                        return;
                    }
                }

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

                string tmpPath = ParticipantsFile + ".tmp";
                string bakPath = ParticipantsFile + ".bak";

                lock (participantsDataLock)
                {
                    File.WriteAllText(tmpPath, serializedData);

                    try
                    {
                        File.Replace(tmpPath, ParticipantsFile, bakPath, /*ignoreMetadataErrors*/ true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(tmpPath, ParticipantsFile, true);
                        File.Delete(tmpPath);
                    }
                    catch (IOException)
                    {
                        File.Copy(tmpPath, ParticipantsFile, true);
                        File.Delete(tmpPath);
                    }
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

        private void UpdatePlayerScore(ulong userId, string actionCode, string actionDescription, BasePlayer victim = null, HitInfo info = null, string attackerName = null, string entityName = "Unknown", bool reverseMessage = false, float? distance = null, string rawEntityType = null, string rawEntityPrefab = null)
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

            int previousScore;
            lock (participantsDataLock)
            {
                previousScore = participant.Score;
                participant.Score += points;
            }

            Puts($"[Debug] {participant.Name} (UserID: {userId}) | Previous Score: {previousScore} | Gained: {points} | New Score: {participant.Score}");

            SaveParticipantsData();

            string savedData = File.ReadAllText(ParticipantsFile);
            Puts($"[Debug] Participants.json after save: {savedData}");

            string pluralS = Math.Abs(points) == 1 ? "" : "s";

            var lang = this.lang;
            string langCode = lang.GetLanguage(userId.ToString());

            BasePlayer playerObj = BasePlayer.FindByID(userId);

            string templateKey = reverseMessage
                ? "KillPlayerWithEntity"
                : actionCode switch
                {
                    "KILL" => "KillPlayerWithEntity",
                    "DEAD" => "KilledByPlayer",
                    "JOKE" => "SelfInflictedDeath",
                    "NPC"  => "KillNPC",
                    "ENT"  => "KillEntity",
                    "BRUH" => "DeathByBRUH",
                    "WHY"  => "KillAnimal",
                    _      => "PlayerScoreUpdate"
                };

            attackerName ??= GetPlayerName(userId);

            foreach (var generic in new[] { "an NPC", "an entity", "an animal" })
            {
                if (actionDescription.Contains(generic) && entityName != "Unknown")
                {
                    actionDescription = actionDescription.Replace(generic, $"a {entityName}");
                    break;
                }
            }

            string articleEntityName = WithIndefiniteArticle(entityName, langCode);

            var placeholders = new Dictionary<string, string>
            {
                { "PlayerName",        GetPlayerName(userId) },
                { "VictimName",        victim?.displayName ?? lang.GetMessage("UnknownEntity", this, langCode) },
                { "AttackerName",      attackerName },
                { "EntityName",        entityName },
                { "ArticleEntityName", articleEntityName },
                { "AttackerType",      WithIndefiniteArticle(entityName, langCode) },
                { "Score",             points.ToString() },
                { "TotalScore",        participant.Score.ToString() },
                { "Action",            actionDescription },
                { "PluralS",           pluralS }
            };

            if (actionCode == "WHY")
            {
                placeholders["Distance"] = distance.HasValue ? distance.Value.ToString("F1") : Configuration.AnimalKillDistance.ToString();
            }

            if (!string.IsNullOrEmpty(rawEntityType) || !string.IsNullOrEmpty(rawEntityPrefab))
            {
                BroadcastLocalized(templateKey, placeholders, (viewer, toks) =>
                {
                    var per = new Dictionary<string,string>(toks);
                    var viewerLang = lang.GetLanguage(viewer.UserIDString);
                    string localizedEntityName = per.TryGetValue("EntityName", out var current) ? current : "Unknown";
                    switch (rawEntityType)
                    {
                        case "animal": localizedEntityName = GetFriendlyAnimalName(rawEntityPrefab, viewer); break;
                        case "npc":    localizedEntityName = GetFriendlyNpcName(rawEntityPrefab, viewer);    break;
                        case "trap":   localizedEntityName = GetFriendlyTrapName(rawEntityPrefab, viewer);   break;
                        case "player": localizedEntityName = victim != null ? victim.displayName : localizedEntityName; break;
                        default:
                            if (!string.IsNullOrEmpty(rawEntityPrefab))
                                localizedEntityName = GetFriendlyNpcName(rawEntityPrefab, viewer);
                            break;
                    }
                    per["EntityName"] = localizedEntityName;
                    per["ArticleEntityName"] = WithIndefiniteArticle(localizedEntityName, viewerLang);
                    per["AttackerType"] = WithIndefiniteArticle(localizedEntityName, viewerLang);
                    if (per.TryGetValue("Action", out var act) && toks.TryGetValue("EntityName", out var oldName) && !string.IsNullOrEmpty(act) && !string.IsNullOrEmpty(oldName))
                        per["Action"] = act.Replace(oldName, localizedEntityName);
                    return per;
                });
            }
            else
            {
                BroadcastLocalized(templateKey, placeholders);
            }

            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                var def = string.IsNullOrEmpty(Configuration.DefaultLanguage) ? "en" : Configuration.DefaultLanguage;

                var discordTokens = new Dictionary<string, string>(placeholders);

                if (discordTokens.TryGetValue("EntityName", out var label) && !string.IsNullOrEmpty(label))
                {
                    var defLabel = ForceLabelToDefaultLanguage(label);
                    discordTokens["EntityName"] = defLabel;

                    discordTokens["ArticleEntityName"] = WithIndefiniteArticle(defLabel, def);
                }

                string discordMsg = Lang(templateKey, null, discordTokens, def);
                SendDiscordMessage(discordMsg);
            }

            LogEvent($"[Event] {participant.Name} received {points} point{pluralS} for {actionDescription}. New total score: {participant.Score}.");
        }

        private string HistoryFile => $"{DataDirectory}/TournamentHistory.json";

        private void SaveTournamentHistory(List<PlayerStats> topPlayers)
        {
            EnsureDataDirectory();

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

            var playerList = string.Join("\n", topPlayers
                .Select((p, i) => $"{i + 1}. {p.Name} ({p.Score} Points)"));

            var tokens = new Dictionary<string, string>
            {
                { "PlayerCount", topPlayers.Count.ToString() },
                { "PlayerList",  playerList }
            };

            BroadcastLocalized("TournamentWinners", tokens);

            string message = Lang("TournamentWinners", null, tokens, Configuration.DefaultLanguage);
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
                EnsureDataDirectory();
                File.WriteAllText(AutoEnrollBlacklistFile, JsonConvert.SerializeObject(autoEnrollBlacklist, Formatting.Indented));
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to save auto enroll blacklist: {ex.Message}");
            }
        }
    #endregion
    #region Notifications Handling
        private string Notify(string templateName, BasePlayer actor, int score = 0, string action = "", string entityType = "", string victimName = "", Dictionary<string, string> extraPlaceholders = null)
        {
            ulong actorId   = actor?.userID ?? 0UL;
            string actorName = actorId != 0UL ? GetPlayerName(actorId) : "Unknown";

            var basePlaceholders = new Dictionary<string, string>
            {
                ["PlayerName"] = actorName,
                ["VictimName"] = victimName,
                ["Score"]      = Math.Abs(score).ToString(),
                ["TotalScore"] = (actorId != 0UL && participantsData.TryGetValue(actorId, out var p)) ? p.Score.ToString() : "0",
                ["EntityType"] = entityType,
                ["Action"]     = action,
                ["PluralS"]    = Math.Abs(score) == 1 ? "" : "s"
            };

            if (extraPlaceholders != null)
            {
                foreach (var kvp in extraPlaceholders)
                    basePlaceholders[kvp.Key] = kvp.Value;
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                string message = Lang(templateName, player, basePlaceholders);
                SendFormattedMessage(player, message);
            }

            return Lang(templateName, actor, basePlaceholders);
        }
        
        private void BroadcastLocalized(
            string templateKey,
            Dictionary<string,string> tokens,
            Func<BasePlayer, Dictionary<string,string>, Dictionary<string,string>> perPlayer = null)
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                var per = perPlayer != null ? perPlayer(p, tokens) : tokens;
                var msg = Lang(templateKey, p, per);
                SendFormattedMessage(p, msg);
            }
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

        private void SendFormattedMessage(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message)) return;

            try
            {
                var formattedMessage = Configuration.ChatFormat.Replace("{message}", message);

                ulong iconId = 0;
                if (!ulong.TryParse(Configuration.ChatIconSteamId, out iconId))
                {
                    iconId = 0;
                }

                player.SendConsoleCommand("chat.add", iconId, Configuration.ChatUsername, formattedMessage);

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
                var formatted = Configuration.ChatFormat.Replace("{message}", message);
                var iconId    = ulong.Parse(Configuration.ChatIconSteamId);
                var name      = Configuration.ChatUsername;

                player.SendConsoleCommand("chat.add", iconId, name, formatted);
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
                var formatted = Configuration.ChatFormat.Replace("{message}", message);
                var iconId    = ulong.Parse(Configuration.ChatIconSteamId);
                var name      = Configuration.ChatUsername;

                foreach (var p in BasePlayer.activePlayerList)
                    p.SendConsoleCommand("chat.add", iconId, name, formatted);
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
    #region Kit Economy Integration
    [PluginReference]
    private Plugin Kits;
    [PluginReference]
    private Plugin Clans;

    private void NormaliseKitPrices()
    {
        Configuration.KitPrices = new Dictionary<string, int>(
            Configuration.KitPrices, StringComparer.OrdinalIgnoreCase);
    }
    
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

            var tokens = new Dictionary<string, string>
            {
                ["PlayerName"] = player.displayName,
                ["KitName"] = kitName,
                ["Price"] = kitPrice.ToString(),
                ["TotalPoints"] = participant.Score.ToString()
            };

            if (!pointsDeducted)
            {
                lock (participantsDataLock)
                {
                    participant.Score -= kitPrice;
                    tokens["TotalPoints"] = participant.Score.ToString();
                }

                SaveParticipantsData();

                PrintWarning($"[RustRoyale] {player.displayName} received kit '{kitName}' without sufficient points. Score forced to {participant.Score}.");
                LogEvent($"{player.displayName} forced into negative score ({participant.Score}) after kit '{kitName}' could not be fully charged.");

                string debtMsg = Lang("KitPurchaseSuccess", null, tokens);
                SendTournamentMessage(debtMsg);

                if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
                {
                    var defaultLang = Configuration.DefaultLanguage;
                    SendDiscordMessage(Lang("KitPurchaseSuccess", null, tokens, defaultLang));
                }

                return;
            }

            SaveParticipantsData();
            tokens["TotalPoints"] = participant.Score.ToString();

            BroadcastLocalized("KitPurchaseSuccess", tokens);
            if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
            {
                var def = Configuration.DefaultLanguage;
                SendDiscordMessage(Lang("KitPurchaseSuccess", null, tokens, def));
            }

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

        private string GetTimeRemainingMessage(BasePlayer player = null)
        {
            if (!isTournamentRunning)
                return Lang("NoTournamentRunning", player);

            TimeSpan remainingTime = tournamentEndTime - DateTime.UtcNow;
            string formattedTime = FormatTimeRemaining(remainingTime);

            return Lang("TimeRemaining", player, new Dictionary<string, string>
            {
                { "Time", formattedTime }
            });
        }

       private void DisplayTimeRemaining()
        {
            TimeSpan remainingTime = tournamentEndTime - DateTime.UtcNow;
            string formattedTime = FormatTimeRemaining(remainingTime);

            Notify("TimeRemaining", null, extraPlaceholders: new Dictionary<string, string>
            {
                { "Time", formattedTime }
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

                    var tokens = new Dictionary<string, string>
                    {
                        { "PlayerName", player.displayName },
                        { "Score", existingParticipant.Score.ToString() },
                        { "PenaltyPoints", Configuration.PenaltyPointsOnExit.ToString() }
                    };

                    string messageKey = Configuration.PenaltyPointsOnExit > 0 ? "RejoinedWithPenalty" : "RejoinedNoPenalty";
                    string message = Lang(messageKey, player, tokens);
                    SendPlayerMessage(player, message);

                    LogEvent($"{player.displayName} rejoined the tournament with existing score: {existingParticipant.Score}.");
                }
                else
                {
                    var tokens = new Dictionary<string, string>
                    {
                        { "PlayerName", player.displayName }
                    };

                    string message = Lang("AlreadyParticipating", player, tokens);
                    SendPlayerMessage(player, message);
                }
            }
            else
            {
                var newParticipant = new PlayerStats(player.userID) { Name = player.displayName };
                participantsData.AddOrUpdate(player.userID,
                    id => new PlayerStats(player.userID) { Name = player.displayName },
                    (id, existing) => existing);

                SaveParticipantsData();

                var tokens = new Dictionary<string, string>
                {
                    { "PlayerName", player.displayName }
                };

                BroadcastLocalized("JoinTournament", tokens);
                if (!string.IsNullOrEmpty(Configuration.DiscordWebhookUrl))
                {
                    var def = Configuration.DefaultLanguage;
                    var discordMsg = Lang("JoinTournament", null, tokens, def);
                    SendDiscordMessage(discordMsg);
                }

                LogEvent($"{player.displayName} joined the tournament as a new participant.");
            }

            if (!participants.Contains(player.userID))
            participants.Add(player.userID);

        }

        [ChatCommand("exit_tournament")]
        private void ExitTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (!participantsData.TryGetValue(player.userID, out var participant))
            {
                var tokens = new Dictionary<string, string>
                {
                    { "PlayerName", player.displayName }
                };

                string message = Lang("NotInTournament", player, tokens);
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

                var tokens = new Dictionary<string, string>
                {
                    { "PlayerName", player.displayName },
                    { "PenaltyPoints", penaltyAmount.ToString() }
                };

                leaveMessage = Lang("LeaveTournamentPenalty", player, tokens);

            }
            else
            {
                LogEvent($"{player.displayName} left the tournament (no penalty applied).");

                var tokens = new Dictionary<string, string>
                {
                    { "PlayerName", player.displayName }
                };

                leaveMessage = Lang("LeaveTournament", player, tokens);

            }

            SaveParticipantsData();

            SendPlayerMessage(player, leaveMessage);
        }

        [ChatCommand("open_tournament")]
        private void OpenTournamentCommand(BasePlayer player, string command, string[] args)
        {
            var tokens = new Dictionary<string, string>
            {
                { "PlayerName", player.displayName }
            };

            if (participantsData.TryGetValue(player.userID, out var existingParticipant))
            {
                if (inactiveParticipants.Remove(player.userID))
                {
                    Puts($"[Debug] {player.displayName} ({player.userID}) re‑enabled from inactive list.");
                    SaveParticipantsData();

                    string reactivateMsg = Lang("ReactivatedTournament", player, tokens);
                    SendPlayerMessage(player, reactivateMsg);

                    LogEvent($"{player.displayName} reactivated entry in the tournament.");
                }
                else
                {
                    Puts($"[Debug] {player.displayName} ({player.userID}) attempted to opt‑in but is already active.");

                    string alreadyMsg = Lang("AlreadyOptedIn", player, tokens);
                    SendPlayerMessage(player, alreadyMsg);
                }
                return;
            }

            var newParticipant = new PlayerStats(player.userID) { Name = player.displayName };
            participantsData.AddOrUpdate(player.userID,
                id => new PlayerStats(player.userID) { Name = player.displayName },
                (id, existing) => existing);

            inactiveParticipants.Remove(player.userID);
            openTournamentPlayers.Add(player.userID);

            Puts($"[Debug] {player.displayName} ({player.userID}) added to participantsData and openTournamentPlayers.");
            SaveParticipantsData();

            string successMsg = Lang("OptedInTournament", player, tokens);
            SendPlayerMessage(player, successMsg);

            LogEvent($"{player.displayName} opted into the tournament.");
        }

        [ChatCommand("close_tournament")]
        private void CloseTournamentCommand(BasePlayer player, string command, string[] args)
        {
            var tokens = new Dictionary<string, string>
            {
                { "PlayerName", player.displayName }
            };

            if (participantsData.ContainsKey(player.userID))
            {
                inactiveParticipants.Add(player.userID);
                autoEnrollBlacklist.Add(player.userID);
                SaveAutoEnrollBlacklist();
                SaveParticipantsData();

                Puts($"[Debug] {player.displayName} ({player.userID}) marked inactive from participantsData and added to auto-enroll blacklist.");

                string message = Lang("OptedOutTournament", player, tokens);
                SendPlayerMessage(player, message);

                LogEvent($"{player.displayName} opted out of the tournament.");
            }
            else
            {
                Puts($"[Debug] {player.displayName} ({player.userID}) was not found in participantsData.");
                Puts($"[Debug] Current participantsData: {string.Join(", ", participantsData.Keys.Select(id => $"{id} ({GetPlayerName(id)})"))}");


                string message = Lang("NotInTournament", player, tokens);
                SendPlayerMessage(player, message);
            }
        }

        [ChatCommand("status_tournament")]
        private void StatusTournamentCommand(BasePlayer player, string command, string[] args)
        {
            if (isTournamentRunning && DateTime.UtcNow >= tournamentEndTime)
            {
                Puts("[Guard] Status check found a past-due tournament; ending now.");
                EndTournament();
            }

            if (isTournamentRunning)
            {
                string timeRemainingMessage = GetTimeRemainingMessage(player);
                var sorted = participantsData.Values.OrderByDescending(p => p.Score).ToList();

                string topScorerMessage = sorted.Any()
                    ? $"{sorted.First().Name} with {sorted.First().Score} points"
                    : Lang("NoTopScorer", player);

                string totalParticipants = sorted.Count.ToString();

                var tokens = new Dictionary<string, string>
                {
                    { "TimeRemaining",     timeRemainingMessage },
                    { "TotalParticipants", totalParticipants    },
                    { "TopScorer",         topScorerMessage     }
                };

                BroadcastLocalized("TournamentStatusActive", tokens);
                string message = Lang("TournamentStatusActive", player, tokens);
                SendPlayerMessage(player, message);
            }
            else
            {
                string timeUntilStart = tournamentStartTime > DateTime.UtcNow
                    ? FormatTimeRemaining(tournamentStartTime - DateTime.UtcNow)
                    : "N/A";

                var tokens = new Dictionary<string, string>
                {
                    { "TimeUntilStart", timeUntilStart }
                };

                BroadcastLocalized("TournamentStatusInactive", tokens);
                string message = Lang("TournamentStatusInactive", player, tokens);
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
                SendPlayerMessage(player, Lang("ScoreFetchError", player));
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
                SendPlayerMessage(player, Lang("NoScoresAvailable", player));
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
            sb.AppendLine(Lang("TopPlayersHeader", player));
            for (int i = 0; i < topPlayers.Count; i++)
                sb.AppendLine($"{i + 1}. {topPlayers[i].Name} ({topPlayers[i].Score} pts)");

            if (topClans.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Lang("TopClansHeader", player));
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

                SendPlayerMessage(player, Lang("ConfigReloaded", player));

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
                SendPlayerMessage(player, Lang("ConfigReloadFailed", player));
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
                SendPlayerMessage(player, Lang("NoTournamentRunning", player));
                return;
            }

            EndTournament();

            SendPlayerMessage(player, Lang("TournamentEndedManually", player));
            Puts($"[Debug] Tournament manually ended by {player.displayName} ({player.UserIDString}).");
        }
        
        [ConsoleCommand("end.tournament")]
        private void CCmdEndTournament(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin)
            {
                Puts("[RustRoyale] You don’t have permission to end the tournament.");
                return;
            }

            if (!isTournamentRunning)
            {
                Puts("[RustRoyale] " + Lang("NoTournamentRunning", null));
                return;
            }

            EndTournament();

            Puts($"[Debug] Tournament manually ended via console by {(arg.Connection != null ? arg.Connection.userid.ToString() : "Console")}.");
        }

        [ChatCommand("show_rules")]
        private void ShowRulesCommand(BasePlayer player, string command, string[] args)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Lang("RulesHeader", player));

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

            sb.AppendLine("\n" + Lang("KitPricesHeader", player));
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

            var helpText = Lang("HelpCommandHeader", player) + "\n";
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
