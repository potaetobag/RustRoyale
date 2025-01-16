RustRoyale revolutionizes Rust gameplay with an action-packed, point-based tournament system designed for everyone—from hardcore competitors to casual players with limited time. Dive into the thrill of dynamic scoring, where every kill, every victory, and every misstep shapes the leaderboard. Whether you're in a different timezone or only have a few hours to play, RustRoyale ensures you're part of the action. With automated scheduling, real-time notifications, and seamless Discord integration, every match feels alive, connected, and unforgettable. Ready to dominate the arena? Let RustRoyale turn your server into a battlefield of champions!

---

## Key Features

### **Dynamic Scoring System**
- Earn **3 points** for eliminating other players. *(KILL)*
- Lose **3 points** when killed by other players. *(DEAD)*
- Deduct **1 point** for deaths due to self-inflicted damage. *(JOKE)*
- Lose **2 points** when killed by NPCs, including Helicopters or Bradley. *(BRUH)*
- Kill NPCs (e.g., Murderers, Zombies, Scientists, Scarecrows) to gain **1 point.** *(NPC)*
- Destroy Helicopters or Bradley Tanks to earn **5 points.** *(ENT)*

---

### **Notifications and Updates**
- Customizable notifications for significant events, including kills, deaths, and score changes.
- Dynamic countdowns and time-based alerts with configurable intervals.
- Real-time updates sent to **global Rust chat** and **Discord.**
- On-demand display of tournament time remaining and scores via chat commands.

![](https://potaetobag.live/imgs/potaetobag-rustroyale-ingame.png)  
![](https://potaetobag.live/imgs/potaetobag-rustroyale-discord.png)

---

### **Automation and Safety Features**
- Automatic tournament scheduling with flexible configurations.
- Safeguards to ensure only valid actions are scored, preventing disruptions by NPCs or environmental factors.
- Thread-safe data handling and periodic backups to ensure reliability.
- Automatic cleanup of outdated tournament data files.

---

### **Advanced Features**
- **Top Players Tracking**: Configurable to highlight top performers at the tournament’s end.
- **Custom Message Templates**: Easily modify system messages to suit your server's tone.
- **Player Name Caching**: Improved performance with dynamic caching of participant names.
- **Historical Data**: Save tournament results, including top players, in history and winners files.
- **Discord Integration**: Real-time tournament notifications sent directly to your Discord server.

---

## Use Cases

RustRoyale fits a variety of playstyles and scenarios, including:

- **For players without time for weekly wipes**: Focus on NPCs and point-scoring, creating a Diablo or Path of Exile-like experience.
- **For regular servers**: Keep players motivated with an engaging score system beyond farming and raiding bases.
- **For creative gameplay styles**: Transform Rust into a unique experience tailored to your server’s goals and community.

---

## Configuration

```json
{
  "DiscordWebhookUrl": "",
  "ChatFormat": "[<color=#d97559>RustRoyale</color>] {message}",
  "ChatIconSteamId": "76561199815164411",
  "ChatUsername": "[RustRoyale]",
  "AutoStartEnabled": true,
  "StartDay": "Friday",
  "StartHour": 14,
  "StartMinute": 0,
  "DurationHours": 72,
  "DataRetentionDays": 30,
  "TopPlayersToTrack": 3,
  "NotificationIntervals": [
    600,
    60
  ],
  "Timezone": "UTC",
  "ScoreRules": {
    "KILL": 3,
    "DEAD": -3,
    "JOKE": -1,
    "NPC": 1,
    "ENT": 5,
    "BRUH": -2
  },
  "MessageTemplates": {
    "StartTournament": "Brace yourselves, champions! The tournament has begun! Time to show off those pro skills (or hilarious fails). Time left: {TimeRemaining}. Duration: {Duration} hours.",
    "EndTournament": "The tournament is over! Congrats to the winners, and for the rest... better luck next time (maybe practice a bit?).",
    "PlayerScoreUpdate": "{PlayerName} just bagged {Score} point{PluralS} for {Action}. Somebody's on fire!",
    "TopPlayers": "Leaderboard time! Top {Count} players are: {PlayerList}. Did your name make the cut, or are you just here for fun?",
    "TimeRemaining": "Tick-tock! Time remaining in the tournament: {Time}. Don't waste it—score some points!",
    "JoinTournament": "{PlayerName} has entered the fray! Grab the popcorn, this should be good.",
    "LeaveTournament": "{PlayerName} has exited the battlefield. Maybe they got scared? We’ll never know.",
    "KillPlayerWithEntity": "{PlayerName} earned {Score} point{PluralS} for eliminating {VictimName} with {EntityName} to respawn land! Total score: {TotalScore}. Savage!",
    "SelfInflictedDeath": "Oops! {PlayerName} lost {Score} point{PluralS} for a self-inflicted oopsie. Total score: {TotalScore}. Smooth move, buddy.",
    "DeathByEntity": "{PlayerName} was defeated by {AttackerType} and lost {Score} point{PluralS}. Ouch! Total score: {TotalScore}",
    "DeathByNPC": "Yikes! {PlayerName} lost {Score} point{PluralS} for getting clobbered by an NPC. Total score: {TotalScore}.",
    "KillEntity": "{PlayerName} earned {Score} point{PluralS} for obliterating a {AttackerType}! Total score: {TotalScore}. BOOM!",
    "KillNPC": "{PlayerName} earned {Score} point{PluralS} for bravely taking down an NPC! Total score: {TotalScore}.",
    "KillPlayer": "{PlayerName} earned {Score} point{PluralS} for sending {VictimName} to respawn land! Total score: {TotalScore}.",
    "KilledByPlayer": "{VictimName} lost {Score} point{PluralS} for being killed by {AttackerName}. Total score: {TotalScore}. Better luck next time!",
    "DeathByBRUH": "{PlayerName} lost {Score} point{PluralS} for getting defeated by {EntityName}. Total score: {TotalScore}. BRUH moment!",
    "NoTournamentRunning": "Hold your horses! There's no tournament right now. Next round starts in {TimeRemainingToStart}. Grab a snack meanwhile!",
    "ParticipantsAndScores": "Scoreboard time! (Page {Page}/{TotalPages}): {PlayerList}. Who’s crushing it? Who’s just chilling?",
    "NotInTournament": "Uh-oh! You’re not part of the tournament. Join in, don’t be shy!",
    "NoPermission": "Sorry, you don’t have permission to {ActionName}. Maybe ask the admins for a favor?",
    "AlreadyParticipating": "Relax, {PlayerName}. You’re already in the tournament. No need to double-dip!",
    "AlreadyOptedIn": "Nice try, {PlayerName}, but you’re already opted in. Eager much?",
    "OptedOutTournament": "{PlayerName} has decided to opt out. Bye-bye! Don’t let FOMO get you.",
    "NotOptedInTournament": "You weren’t even opted in, {PlayerName}. Why so dramatic?",
    "TournamentNotRunning": "Patience is a virtue, {PlayerName}. No tournament now. Next round starts in {TimeRemainingToStart}. Go sharpen your skills!",
    "TournamentScores": "Here’s the rundown of scores: {PlayerList}. Is your name shining, or are you just here for the jokes?",
    "TournamentAlreadyRunning": "Whoa there! A tournament is already underway. Time left: {TimeRemaining}. Jump in or cheer from the sidelines!",
    "NoScores": "No scores available yet. Join the tournament and make some history!",
    "TournamentAboutToStart": "The tournament is about to start! Opt-in now to participate.",
    "TournamentCountdown": "Tournament starting soon! {Time} left to join."
  }
}
```

---

## Command Suite

- `/start_tournament` – Start a tournament manually.
- `/end_tournament` – End an ongoing tournament and announce winners.
- `/time_tournament` – View the remaining tournament time.
- `/score_tournament` – Display current player scores.
- `/enter_tournament` – Join the tournament.
- `/exit_tournament` – Leave the tournament.
- `/open_tournament` – Enable automatic participation in future tournaments.
- `/close_tournament` – Disable automatic participation in future tournaments.
- `/status_tournament` – View tournament status, scores, and countdowns.

---

## Permissions

```bash
oxide.grant user SteamID rustroyale.admin
oxide.revoke user SteamID rustroyale.admin
```

---

## Donations

[![patreon](https://img.shields.io/badge/donate-patreon-orange)](https://www.patreon.com/c/Potaetobag)
[![ko-fi](https://img.shields.io/badge/donate-kofi-red)](https://ko-fi.com/potaetobag)
[![pally](https://img.shields.io/badge/donate-pally-purple)](https://pally.gg/p/potaetobag)

---

## Future Plans

- **Custom Scoring Rules**: Enable admins to define their own scoring actions (e.g., points for building or defending structures).
- **In-Game Achievements**: Introduce rewards for milestones, such as the most NPC kills or longest streak without dying.
- **Team-Based Tournaments**: Add support for team-based tournaments where scores are aggregated by teams.
- **Region-Based Notifications**: Allow announcements in specific in-game regions for immersive gameplay.
- **Live Scoreboards**: Display real-time leaderboards on in-game signs or monitors.
- **Streaming Integration**: Provide tools to showcase tournament updates during live streams on Twitch or YouTube.
- **Daily Challenges**: Add short-term objectives like "Kill 10 NPCs in 15 minutes" to engage players during downtime.
- **Economy Integration**: Integrate tournaments with in-game economies to reward redeemable points or items.

---

RustRoyale is your solution to engaging, competitive gameplay that transforms Rust into an exciting, community-driven experience. Install it today to enhance your server’s dynamic and bring your players closer together!
