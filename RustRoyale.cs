using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Libraries;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RustRoyale", "Potaetobag", "1.0.0"), Description("Rust Royale custom tournament game mode with point-based scoring system.")]
    class RustRoyale : RustPlugin
    {
        #region Configuration
        private ConfigData Configuration;

        private class ConfigData
        {
            public string DiscordWebhookUrl { get; set; } = "";
            public string ChatIconSteamId { get; set; } = "76561198332731279"; // Default Steam ID for the icon
            public string ChatUsername { get; set; } = "[RustRoyale]"; // Default username for chat messages
            public bool AutoStartEnabled { get; set; } = true;
            public string StartDay { get; set; } = "Friday"; // Default to Friday
            public int DurationHours { get; set; } = 72; // Default duration is 72 hours
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
        #endregion

        #region Permissions
        private const string AdminPermission = "rustroyale.admin";

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
        }
        #endregion

        #region Tournament Logic
        private readonly Dictionary<ulong, PlayerStats> playerStats = new Dictionary<ulong, PlayerStats>();
        private bool isTournamentRunning = false;
        private DateTime tournamentEndTime;

        private void StartTournament()
        {
            if (isTournamentRunning)
            {
                TimeSpan remainingTime = tournamentEndTime - DateTime.UtcNow;
                SendGlobalChatMessage($"A tournament is already running! Time remaining: {remainingTime:hh\\:mm\\:ss}");
                return;
            }

            Puts("RustRoyale: Tournament started!");
            isTournamentRunning = true;
            tournamentEndTime = DateTime.UtcNow.AddHours(Configuration.DurationHours);

            playerStats.Clear();
            SendDiscordMessage($"Tournament has started! Let the games begin! (Time remaining: {Configuration.DurationHours} hours)");
            SendGlobalChatMessage($"Tournament has started! Let the games begin! (Time remaining: {Configuration.DurationHours} hours)");
        }

        private void EndTournament()
        {
            if (!isTournamentRunning)
            {
                SendGlobalChatMessage("No tournament is currently running to end!");
                return;
            }

            Puts("RustRoyale: Tournament ended!");
            isTournamentRunning = false;

            AnnounceWinners();
            SendDiscordMessage("Tournament has ended! Winners are being announced.");
            SendGlobalChatMessage($"Tournament has ended! Winners are being announced.");
        }

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            if (!isTournamentRunning) return;

            if (victim == null) return;

            var attacker = info?.InitiatorPlayer;

            bool isVictimNPC = victim.ShortPrefabName.Contains("murderer") ||
                               victim.ShortPrefabName.Contains("zombie") ||
                               victim.ShortPrefabName.Contains("scientist") ||
                               victim.ShortPrefabName.Contains("scarecrow");

            bool isVictimEntity = victim.ShortPrefabName.Contains("helicopter") ||
                                  victim.ShortPrefabName.Contains("bradley");

            if (isVictimNPC)
            {
                if (attacker != null && !attacker.IsNpc)
                {
                    UpdatePlayerScore(attacker.userID, "NPC");
                    string npcKillMessage = $"{attacker.displayName} received 1 point for killing an NPC. Total score: {playerStats[attacker.userID].Score}";
                    SendGlobalChatMessage(npcKillMessage);
                    SendDiscordMessage(npcKillMessage);
                }
                return;
            }

            if (isVictimEntity)
            {
                if (attacker != null && !attacker.IsNpc)
                {
                    UpdatePlayerScore(attacker.userID, "ENT");
                    string entityKillMessage = $"{attacker.displayName} received 5 points for destroying a Helicopter or Bradley. Total score: {playerStats[attacker.userID].Score}";
                    SendGlobalChatMessage(entityKillMessage);
                    SendDiscordMessage(entityKillMessage);
                }
                return;
            }

            if (attacker == null || attacker.IsNpc)
            {
                if (victim != null)
                {
                    UpdatePlayerScore(victim.userID, "JOKE");
                    string npcDeathMessage = $"{victim.displayName} lost 1 point for being killed by an NPC. Total score: {playerStats[victim.userID].Score}";
                    SendGlobalChatMessage(npcDeathMessage);
                    SendDiscordMessage(npcDeathMessage);
                }
                return;
            }

            if (attacker == victim)
            {
                UpdatePlayerScore(victim.userID, "JOKE");
                string selfInflictedMessage = $"{victim.displayName} lost 1 point for self-inflicted death. Total score: {playerStats[victim.userID].Score}";
                SendGlobalChatMessage(selfInflictedMessage);
                SendDiscordMessage(selfInflictedMessage);
                return;
            }

            UpdatePlayerScore(victim.userID, "DEAD");
            string victimDeathMessage = $"{victim.displayName} lost 3 points for being killed by {attacker.displayName}. Total score: {playerStats[victim.userID].Score}";
            SendGlobalChatMessage(victimDeathMessage);
            SendDiscordMessage(victimDeathMessage);

            UpdatePlayerScore(attacker.userID, "KILL");
            string attackerKillMessage = $"{attacker.displayName} received 3 points for killing {victim.displayName}. Total score: {playerStats[attacker.userID].Score}";
            SendGlobalChatMessage(attackerKillMessage);
            SendDiscordMessage(attackerKillMessage);
        }

        private void UpdatePlayerScore(ulong userId, string actionCode)
        {
            if (!isTournamentRunning) return;

            if (!playerStats.ContainsKey(userId))
                playerStats[userId] = new PlayerStats(userId);

            if (Configuration.ScoreRules.TryGetValue(actionCode, out int points))
            {
                playerStats[userId].Score += points;
            }
        }

        private void AnnounceWinners()
        {
            var sortedPlayers = new List<PlayerStats>(playerStats.Values);
            sortedPlayers.Sort((a, b) => b.Score.CompareTo(a.Score));

            foreach (var player in BasePlayer.activePlayerList)
            {
                player.ChatMessage("Tournament Results:");
                for (int i = 0; i < Math.Min(3, sortedPlayers.Count); i++)
                {
                    string message = $"{i + 1}. {sortedPlayers[i].Name} - {sortedPlayers[i].Score} Points";
                    player.ChatMessage(message);
                }
            }

            string discordMessage = "Tournament Results: ";
            for (int i = 0; i < Math.Min(3, sortedPlayers.Count); i++)
            {
                discordMessage += $"{i + 1}. {sortedPlayers[i].Name} - {sortedPlayers[i].Score} Points ";
            }
            SendDiscordMessage(discordMessage);
        }

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
            Puts(message);
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
            DisplayTimeRemaining();
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
