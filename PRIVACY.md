[한국어](PRIVACY.ko.md)

# Privacy — Pokémon: Legends of Sinnoh Digital Edition play records

The PC version of this game collects **play records** anonymously to improve the AI. It tells you once on first launch and is on by default. You can turn it off at any time in Settings. This notice has not been reviewed by a lawyer.

## What is sent

- Sent: the game's command log (chosen actions and answers — numbers and labels only), game seed and AI seed, game options and room settings, each seat's protagonist and PC/web flag, game, rules and save format versions, and an anonymous player ID created by Unity Authentication.
- Not sent (removed before sending): nicknames, rejoin tokens, ban tokens, save names and save times.
- Processed by Unity: your IP address when connecting (Unity Gaming Services processes it to provide the service; this game does not collect it separately).
- When: when a game that had a Team Galactic battle ends, or when you leave it. In rooms only the host PC sends records (the lobby shows a notice line when the host has it on). Guest PCs and the web guest version send nothing.

## Bug reports

- Sent only when you press Send in [Report a Bug] (game menu, or Settings on the title screen) — whatever the play records setting, because you chose to send it. The window lists what is included.
- Sent: your description, game, rules and protocol versions, screen language, window size, the game log (the recent part of Player.log — up to 256 KB, with nicknames, rejoin tokens and your user folder path masked), the current game's record if any (with nicknames and rejoin tokens removed, as for play records), and whether you are host or guest in a room. One report is at most 1 MB compressed. It goes to the same storage (Cloud Save) under your anonymous player ID. The web guest version cannot send reports.

## Room chat

- Chat (only when the host turns it on in room settings) travels only between the players in that room (internet rooms pass it through Unity Relay like the rest of the game). It is not saved or sent anywhere else, and is never included in saves, play records, bug reports or logs.

## Where and how long

- Storage: Unity Gaming Services **Cloud Save** (Player Files). See Unity's policy at https://unity.com/legal/privacy-policy.
- Retention: no fixed period. At most the latest 200 files (game records and bug reports together) are kept per player; beyond that the oldest is deleted first. The developer uses them only for AI test runs (replaying games people played) and fixing bugs, and does not share them.

## Turning off, deleting, contact

- Turn off: Settings → "Play Records" → Off. Nothing is sent after that.
- Delete: Settings → [Delete My Records] → [Delete]. This removes all of your records (play records and bug reports) from the server (needs an internet connection).
- Contact: [GitHub Issues](https://github.com/Rekkons/legends-of-sinnoh/issues). Issues are public, so please do not post personal information there.
