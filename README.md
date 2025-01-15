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
  "ChatIconSteamId": "76561199815164411",
  "ChatUsername": "[RustRoyale]",
  "AutoStartEnabled": true,
  "StartDay": "Friday",
  "StartHour": 14,
  "DurationHours": 72,
  "DataRetentionDays": 30,
  "TopPlayersToTrack": 3,
  "NotificationIntervals": [600, 60],
  "ScoreRules": {
    "KILL": 3,
    "DEAD": -3,
    "JOKE": -1,
    "NPC": 1,
    "ENT": 5
  },
  "MessageTemplates": {
    "StartTournament": "The tournament has started! Good luck to all participants! Time left: {TimeRemaining}.",
    "EndTournament": "The tournament has ended! Congratulations to the top players!",
    "PlayerScoreUpdate": "{PlayerName} earned {Score} point{PluralS} for {Action}.",
    "TopPlayers": "Top {Count} players: {PlayerList}.",
    "TimeRemaining": "Time remaining in the tournament: {Time}."
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

## What's New in Version 1.0.6?

- **Updated Scoring Logic**: Improved handling of NPC kills and self-inflicted deaths, ensuring fair point allocation.
- **Expanded NPC Recognition**: Added support for new NPC types, including underwater and tunnel dwellers.
- **Real-Time Name Updates**: Automatically updates participant names when they log in or out.
- **Dynamic Tournament Messaging**: Enhanced real-time notifications for better engagement in Rust chat and Discord.
- **Robust Data Handling**: Improved participant data storage and recovery mechanisms for increased reliability.
- **Discord Debug Logs**: Added detailed debug logs for Discord notifications.
- **Historical Data Storage**: Saves winners and tournament history in structured files for long-term tracking.

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
