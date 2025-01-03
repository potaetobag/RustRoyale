RustRoyale is a custom tournament plugin for Rust, ideal for players with limited time or those in different timezones. It enables competitive gameplay with a point-based system, allowing asynchronous participation. Features include automated scheduling, score tracking, and Discord integration for updates.
# Key Features:
## Dynamic Scoring System:

* Players earn 3 points for eliminating other players. (KILL)
* Players lose 3 points when killed by other players. (DEAD)
* Deaths due to NPCs, traps, or self-inflicted damage deduct 1 point. (JOKE)
* Killing NPCs (e.g., Murderers, Zombies, Scientists, Scarecrows) awards 1 point. (NPC)
* Destroying large entities like Helicopters or Bradley Tanks grants 5 points. (ENT)

## Notifications and Updates:

* Sends updates to the global Rust chat and Discord, ensuring participants and spectators are informed about significant events like kills, deaths, and score changes.
* Displays the tournament time remaining and scores on demand via chat commands.

![](https://potaetobag.live/imgs/potaetobag-rustroyale-ingame.png)

![](https://potaetobag.live/imgs/potaetobag-rustroyale-discord.png)

## Automation and Safety Features:

* Automatically starts tournaments on a configurable schedule.
* Ensures only valid actions are scored, with safeguards to prevent NPCs or environmental interactions from disrupting the score system.

## Configuration:
```
{
  "DiscordWebhookUrl": "",
  "ChatIconSteamId": "76561198332731279",
  "ChatUsername": "[RustRoyale]",
  "AutoStartEnabled": true,
  "StartDay": "Friday",
  "DurationHours": 72,
  "ScoreRules": {
    "KILL": 3,
    "DEAD": -3,
    "JOKE": -1,
    "NPC": 1,
    "ENT": 5
  }
}
```

## Command Suite:

* /start_tournament – Manually start a tournament.
* /end_tournament – End an ongoing tournament and announce winners.
* /time_tournament – Check the remaining time in the tournament.
* /score_tournament – Display the current scores of all players.

## Permissions:
```
oxide.grant user SteamID rustroyale.admin
oxide.revoke user SteamID rustroyale.admin
```

## What's Next?
* Find, steal, or destroy the opponent's token to score points. This strategy is especially useful when there’s a large point difference. A quick find might save you from losing, but your opponent could have the best defense, putting you in an even worse position. (DSS)
* Earn points for building structures that deceive your opponent and hide the token. This tactic can make your opponent waste time searching for the token, ultimately losing the tournament by failing to match your score. (DISS)
* Penalties for camping outside designated safe zones include temporary server time-outs and a reduction or freeze of accumulated points. (BAD)
* The player will be granted 3 points for a clan headshot done to an animal or NPC over a long distance ( 150 meters). (SHOT)

RustRoyale fosters competitive and engaging gameplay, combining dynamic mechanics with a seamless interface for managing tournaments. It’s a must-have for server administrators looking to elevate player engagement and interaction.
