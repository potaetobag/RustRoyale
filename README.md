RustRoyale is a custom tournament plugin for Rust, ideal for players with limited time or those in different timezones. It enables competitive gameplay with a point-based system, allowing asynchronous participation. Features include automated scheduling, advanced notifications, enhanced score tracking, and Discord integration for real-time updates.

---

## Key Features

### **Dynamic Scoring System**
- Earn 3 points for eliminating other players. *(KILL)*
- Lose 3 points when killed by other players. *(DEAD)*
- Deduct 1 point for deaths due to NPCs, traps, or self-inflicted damage. *(JOKE)*
- Kill NPCs (e.g., Murderers, Zombies, Scientists, Scarecrows) to gain 1 point. *(NPC)*
- Destroy Helicopters or Bradley Tanks to earn 5 points. *(ENT)*

---

### **Notifications and Updates**
- Customizable notifications for significant events, including kills, deaths, and score changes.
- Dynamic countdowns and time-based alerts with configurable intervals.
- Real-time updates sent to global Rust chat and Discord.
- On-demand display of tournament time remaining and scores via chat commands.

![](https://potaetobag.live/imgs/potaetobag-rustroyale-ingame.png)
![](https://potaetobag.live/imgs/potaetobag-rustroyale-discord.png)

---

### **Automation and Safety Features**
- Automatic tournament scheduling with flexible configurations.
- Safeguards to ensure only valid actions are scored, preventing score disruptions by NPCs or environmental factors.
- Thread-safe data handling and periodic backups to ensure reliability.
- Automatic cleanup of outdated tournament data files.

---

### **Advanced Features**
- **Top Players Tracking**: Configurable to highlight the top performers at the tournament’s end.
- **Customizable Message Templates**: Easily modify system messages to suit your server's tone.
- **Player Name Caching**: Improved performance with dynamic caching of participant names.
- **Historical Data**: Save tournament results, including top players, in separate history and winners files.
- **Pagination for Scores**: Simplified management of large tournaments with paginated participant score display.

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
- `/status_tournament` – View tournament status, scores, and countdowns with pagination.

---

## Permissions
```bash
oxide.grant user SteamID rustroyale.admin
oxide.revoke user SteamID rustroyale.admin
```

---

## What's New in Version 1.0.3?
- **Top Players Tracking**: Configure how many top players are highlighted post-tournament.
- **Customizable Notifications**: Dynamic notifications with configurable time intervals.
- **Message Templates**: Tailor system messages to fit your server's style.
- **Improved Countdown Logic**: Simplified and efficient tournament scheduling and reminders.
- **Enhanced Score Display**: Paginated score view for better management in large tournaments.
- **Data Safety**: Thread-safe participant data handling and periodic backups.
- **Historical Data**: Export tournament history and winners for long-term tracking.

---

## Future Plans
- Introduce new scoring opportunities for advanced player actions.
- Add penalties for camping and unsportsmanlike behavior.
- Reward skillful actions like long-distance headshots on NPCs or animals.

---

RustRoyale fosters competitive and engaging gameplay, combining dynamic mechanics with a seamless interface for managing tournaments. It’s a must-have plugin for server administrators looking to elevate player interaction and community engagement!
