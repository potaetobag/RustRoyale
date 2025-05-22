# RustRoyale

RustRoyale revolutionizes Rust gameplay with an action-packed, point-based tournament system designed for everyone—from hardcore competitors to casual players with limited time. Dive into the thrill of dynamic scoring, where every kill, every victory, and every misstep shapes the leaderboard. Whether you're in a different timezone or only have a few hours to play, RustRoyale ensures you're part of the action. With automated scheduling, real-time notifications, and seamless Discord integration, every match feels alive, connected, and unforgettable. Ready to dominate the arena? Let RustRoyale turn your server into a battlefield of champions!

---

## 🔹 Key Features

### ✨ **Dynamic Scoring System**

* Earn **3 points** for eliminating other players. *(KILL)*
* Lose **3 points** when killed by other players. *(DEAD)*
* Deduct **1 point** for deaths due to self-inflicted damage. *(JOKE)*
* Lose **2 points** when killed by NPCs, including Helicopters or Bradley. *(BRUH)*
* Kill NPCs (e.g., Murderers, Zombies, Scientists, Scarecrows) to gain **1 point.** *(NPC)*
* Destroy Helicopters or Bradley Tanks to earn **5 points.** *(ENT)*
* Kill animals from a long distance to earn **5 points.** *(WHY)*

---

### 🔔 **Notifications and Updates**

* Customizable notifications for significant events, including kills, deaths, and score changes.
* Dynamic countdowns and time-based alerts with configurable intervals.
* Real-time updates sent to **global Rust chat** and **Discord.**
* On-demand display of tournament time remaining and scores via chat commands.

![](https://potaetobag.live/imgs/potaetobag-rustroyale-ingame.png)
![](https://potaetobag.live/imgs/potaetobag-rustroyale-discord.png)

---

### 🚀 **Automation and Safety Features**

* Automatic tournament scheduling with flexible configurations.
* Safeguards to ensure only valid actions are scored, preventing exploits.
* Thread-safe data handling with periodic backups.
* Automatic cleanup of outdated tournament data files.
* Persistent tournament state even after server restarts.

---

### 🧪 **Kit Integration and In-Game Economy**

* Full **Kits plugin** support using points as currency.
* Redeem kits using earned points even when your balance is negative.
* Five customizable kits: *Starter*, *Bronze*, *Silver*, *Gold*, *Platinum*.
* Configurable pricing per kit in the config.

---

### 🌎 **NPC, Animal, and Trap Kill Points**

* Points for:

  * NPC kills (Scientists, Murderers, Scarecrows, etc.)
  * Trap/Turret kills with player attribution.
  * Long-distance animal kills.
* Friendly name translation for chat messages ("an NPC" instead of prefab name).

---

### 🌐 **UI and UX Enhancements**

* Fully featured in-game configuration UI.
* Real-time config edits without server reload.
* Scrollable **Welcome UI** shown on player join.
* UI includes tournament overview, scoring rules, and kit list.
* Players can opt out from seeing the Welcome UI.

---

### 🔗 **Discord Integration**

* All major events (start, end, kills, top players) can be broadcast to Discord.
* Templated messages with placeholders.
* Instant webhook delivery for remote visibility.

---

### 📅 **Customization & Localization**

* All chat and UI messages are templated in config.
* Easily localize or re-theme your server messaging.
* Messages use dynamic placeholders with plural logic and proper grammar.

---

### 👥 **Player Management and Permissions**

* Player name caching for reduced performance overhead.
* Participant scores and history are stored per user.
* Admin-only features gated by `rustroyale.admin` permission.

---

## 📆 Use Cases

RustRoyale fits a variety of playstyles and scenarios, including:

* **For players without time for weekly wipes**: Focus on NPCs and point-scoring.
* **For regular servers**: Keep players engaged with competitive objectives.
* **For creative gameplay styles**: Customize the experience with unique score rewards.

---

## ⚙️ Configuration and Example

```json
{
  "DiscordWebhookUrl": "",
  "ChatFormat": "[<color=#d97559>RustRoyale</color>] {message}",
  "ChatIconSteamId": "76561199815164411",
  "ChatUsername": "[RustRoyale]",
  "Timezone": "Central Standard Time",
  "StartDay": "Thursday",
  "AutoStartEnabled": false,
  "AutoEnrollEnabled": true,
  "PenaltyOnExitEnabled": true,
  "ShowWelcomeUI": true,
  "EnableAutoUpdate": false,
  "PenaltyPointsOnExit": 25,
  "StartHour": 15,
  "StartMinute": 0,
  "DurationHours": 620,
  "DataRetentionDays": 30,
  "TopPlayersToTrack": 3,
  "TopClansToTrack": 3,
  "JoinCutoffHours": 0,
  "NotificationIntervals": [
    600,
    60
  ],
  "ScoreRules": {
    "KILL": 3,
    "DEAD": -3,
    "JOKE": -1,
    "NPC": 1,
    "ENT": 25,
    "BRUH": -2,
    "WHY": 5
  },
  "AnimalKillDistance": 100.0,
  "MessageTemplates": {
    "StartTournament": "Brace yourselves, champions! The tournament has begun! Time to show off those pro skills (or hilarious fails). Time left: {TimeRemaining}. Duration: {Duration} hours.",
    "EndTournament": "The tournament is over! Congrats to the winners, and for the rest... better luck next time (maybe practice a bit?).",
    "ResumeTournament": "Welcome back, warriors! The tournament has been resumed. Time left: {TimeRemaining}. Duration: {Duration} hours.",
    "PlayerScoreUpdate": "{PlayerName} just bagged {Score} point{PluralS} for {Action}. Somebody's on fire!",
    "TopPlayers": "Leaderboard time! Top {Count} players are: {PlayerList}. Did your name make the cut, or are you just here for fun?",
    "TimeRemaining": "Tick-tock! Time remaining in the tournament: {Time}. Don't waste it—score some points!",
    "JoinTournament": "{PlayerName} has entered the fray! Grab the popcorn, this should be good.",
    "LeaveTournament": "{PlayerName} has exited the battlefield. Maybe they got scared? We’ll never know.",
    "KitPurchaseSuccess": "{PlayerName} has successfully purchased the {KitName} kit for {Price} points. Your new balance is {TotalPoints} points.",
    "KillPlayerWithEntity": "{PlayerName} earned {Score} point{PluralS} for eliminating {VictimName} with {EntityName} to respawn land! Total score: {TotalScore}. Savage!",
    "SelfInflictedDeath": "Oops! {PlayerName} lost {Score} point{PluralS} for a self-inflicted oopsie. Total score: {TotalScore}. Smooth move, buddy.",
    "KillEntity": "{PlayerName} earned {Score} point{PluralS} for obliterating {ArticleEntityName}! Total score: {TotalScore}. BOOM!",
    "KillNPC": "{PlayerName} earned {Score} point{PluralS} for bravely taking down {ArticleEntityName}! Total score: {TotalScore}.",
    "KilledByPlayer": "{VictimName} lost {Score} point{PluralS} for being killed by {AttackerName}. Total score: {TotalScore}. Better luck next time!",
    "DeathByBRUH": "{PlayerName} lost {Score} point{PluralS} for getting defeated by {EntityName}. Total score: {TotalScore}. BRUH moment!",
    "KillAnimal": "{PlayerName} earned {Score} point{PluralS} for killing {ArticleEntityName} from over {Distance} meters away! Total score: {TotalScore}.",
    "NotInTournament": "Uh-oh! You’re not part of the tournament. Join in, don’t be shy!",
    "NoPermission": "Sorry, you don’t have permission to {ActionName}. Maybe ask the admins for a favor?",
    "AlreadyParticipating": "Relax, {PlayerName}. You’re already in the tournament. No need to double-dip!",
    "AlreadyOptedIn": "Nice try, {PlayerName}, but you’re already opted in. Eager much?",
    "OptedOutTournament": "{PlayerName} has decided to opt out. Bye-bye! Don’t let FOMO get you.",
    "TournamentAlreadyRunning": "Whoa there! A tournament is already underway. Time left: {TimeRemaining}. Jump in or cheer from the sidelines!",
    "NoScores": "No scores available yet. Join the tournament and make some history!",
    "TournamentAboutToStart": "The tournament is about to start! Opt-in now to participate.",
    "TournamentCountdown": "Tournament starting soon! {Time} left to join.",
    "LeaveTournamentPenalty": "{PlayerName} left the tournament and lost {PenaltyPoints} points!",
    "DeathByEntity": "{PlayerName} was defeated by {AttackerType} and lost {Score} point{PluralS}. Ouch! Total score: {TotalScore}",
    "DeathByNPC": "Yikes! {PlayerName} lost {Score} point{PluralS} for getting clobbered by an NPC. Total score: {TotalScore}.",
    "KillPlayer": "{PlayerName} earned {Score} point{PluralS} for sending {VictimName} to respawn land! Total score: {TotalScore}.",
    "NoTournamentRunning": "Hold your horses! There's no tournament right now. Next round starts in {TimeRemainingToStart}. Grab a snack meanwhile!",
    "ParticipantsAndScores": "Scoreboard time! (Page {Page}/{TotalPages}): {PlayerList}. Who’s crushing it? Who’s just chilling?",
    "NotOptedInTournament": "You weren’t even opted in, {PlayerName}. Why so dramatic?",
    "TournamentNotRunning": "Patience is a virtue, {PlayerName}. No tournament now. Next round starts in {TimeRemainingToStart}. Go sharpen your skills!",
    "TournamentScores": "Here’s the rundown of scores: {PlayerList}. Is your name shining, or are you just here for the jokes?"
  },
  "KitPrices": {
    "Starter": 5,
    "Bronze": 25,
    "Silver": 50,
    "Gold": 75,
    "Platinum": 100
  }
}
```

---

## ⚖️ Commands

* `/start_tournament` – Manually start a tournament.
* `/end_tournament` – End the current tournament.
* `/time_tournament` – Show time left.
* `/score_tournament` – Show scoreboard.
* `/enter_tournament` – Join the tournament.
* `/exit_tournament` – Leave the tournament.
* `/open_tournament` – Auto-enter future tournaments.
* `/close_tournament` – Disable auto-entry.
* `/status_tournament` – Show current status and stats.
* `/config_tournament` – Open configuration UI.

---

## 👤 Permissions

```bash
oxide.grant user SteamID rustroyale.admin
oxide.revoke user SteamID rustroyale.admin
```

---

## 🌟 Donations

[![patreon](https://img.shields.io/badge/donate-patreon-orange)](https://www.patreon.com/c/Potaetobag)
[![ko-fi](https://img.shields.io/badge/donate-kofi-red)](https://ko-fi.com/potaetobag)
[![pally](https://img.shields.io/badge/donate-pally-purple)](https://pally.gg/p/potaetobag)

---

## 🧰 Future Roadmap

* Custom Scoring Events (e.g., base defense bonuses).
* In-game Achievements and Milestones.
* Team-Based Tournaments with shared scores.
* Region-specific announcements.
* Real-time scoreboard integration on signs.
* Streamer-friendly score feeds.
* Daily mini-objectives to drive engagement.
* Deeper in-game economy integration.

---

RustRoyale is your solution to engaging, competitive gameplay that transforms Rust into an exciting, community-driven experience. Install it today to enhance your server’s dynamic and bring your players closer together!
