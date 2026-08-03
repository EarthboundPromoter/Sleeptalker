# Sleeptalker

**Version 0.8.0 — beta**

A screen-reader mod for **Citizen Sleeper 2: Starward Vector**: it speaks the
game's interface, dice, clocks, contracts, and story through NVDA or JAWS (via
[Tolk](https://github.com/dkager/tolk)) so the game can be played without
sight, start to finish, entirely by keyboard. Sibling project to
[Citizen Speaker](https://github.com/EarthboundPromoter/Citizen-Speaker), the
accessibility mod for the first Citizen Sleeper.

Beta — the whole game surface is built, and a systematic live-play
verification pass is underway. Report any problems to me on Discord or in the
Audiogames forum thread for the game.

## About Citizen Sleeper 2

From the [Steam store page](https://store.steampowered.com/app/2442460/Citizen_Sleeper_2_Starward_Vector/):

> The highly anticipated sequel to one of 2022's most acclaimed RPGs, Citizen Sleeper 2: Starward Vector takes players to the Starward Belt, a richly realised, ramshackle set of habitats in an asteroid belt full of secrets, stories, and characters trying to make ends meet.
>
> You are a sleeper, an emulation of a human mind housed in an artificial body. You are on the run from the corporation that made you and the gang that seeks to control you. Commandeer a ship, build a network of crew and allies, and take on challenging contracts as you seek to build a future for yourself.
>
> Choose a class, configure your skills and assemble your crew in unique tabletop-inspired gameplay. Your future depends on the roll of a dice, as you make difficult choices in a complex world. Reinventing the award-winning systems of Citizen Sleeper, this dice-driven RPG will satisfy both fans of the original game and new players alike.
>
> To stay one step ahead of your pursuers you'll need three things: A belt-worthy ship, a tight crew and a contract or two.
>
> — [Citizen Sleeper 2 on Steam](https://store.steampowered.com/app/2442460/Citizen_Sleeper_2_Starward_Vector/)

## Requirements

- **[Citizen Sleeper 2: Starward Vector](https://store.steampowered.com/app/2442460/Citizen_Sleeper_2_Starward_Vector/)**
  (Steam, Windows).
- A screen reader — **[NVDA](https://www.nvaccess.org/download/)** or **JAWS**
  (or any other reader supported by [Tolk](https://github.com/dkager/tolk)).
- Everything else — the [BepInEx 5](https://github.com/BepInEx/BepInEx) mod
  loader and the Tolk speech DLLs — is bundled in the release zip.
- Start your screen reader *before* launching the game.

## Installing

1. Download the latest Sleeptalker zip from the
   [Releases page](https://github.com/EarthboundPromoter/Sleeptalker/releases/latest).
2. Extract it into the Citizen Sleeper 2 game folder (the one containing
   `Citizen Sleeper 2.exe`), merging folders if asked. The zip carries the
   BepInEx mod loader with the mod already in place, plus the speech DLLs.
3. Launch the game. You'll hear "Sleeptalker 0.8.0." To update, extract the
   newer zip the same way.

## How the mod works

Everything the game shows, it speaks: dialogue reads automatically with
dialogue choices and skill checks, dice, clocks, meters and outcomes are
announced as they change, and menus talk as you move through them.

Stations, your ship, and contract sites are browsed as **tables**: Up and Down
walk the rows — locations, characters, and clocks in physical order, with the
camera following — Left and Right step through a row's facets, Space reads the
whole row, and Enter goes there or starts the action. At an action, pick a die
with the arrows and Enter to slot it; results, clock ticks, and every resource
change are spoken as they resolve, and press N anytime to replay the last
location change.

The belt map (M) is organized around what you can actually do: your current
location first, then everywhere **in reach** in cheapest-fuel order, then
everywhere out of reach with the reason you can't travel there. Enter commits
travel. Your class's **Push** ability lives on P, with the game's own
two-press confirm; contracts speak their stress bar, crises as they trigger,
and the crew task board as you assign it. Each new cycle opens with a summary
of what changed while you slept.

F1 always opens a key table for the current screen: Up and Down walk the
bindings, F1 or Backspace closes it.

## Keys

| Key | Function |
|-----|----------|
| **Arrows** | Move through rows and columns (tables, dialogue, menus, die picker, map). |
| **Enter** | Activate / commit the current row. |
| **Space** | Read the full current row or focused element. |
| **Backspace** | Back / cancel. |
| **1–9** | Pick a dialogue response. |
| **V** | Top bar — vitals, buttons, and dice as walkable rows; V or Backspace closes. |
| **M** | The belt map (or the local map, wherever the game offers one). |
| **G** | Swap between the station and your ship. |
| **U** | Character sheet (skills and upgrades). |
| **I** | Inventory. |
| **J** | Drives (quests); Slash swaps tabs. |
| **P** | Push. |
| **N** | Last location change. |
| **Z** | Repeat last speech. |
| **F1** | Contextual key table. |
| **F3** | Write a diagnostic snapshot to the log (useful in bug reports). |
| **Esc** | Pause (the game's own). |

## What's tested and what isn't

The whole surface is built: the station loop, dialogue, dice and push, the
belt map and travel, contracts with crew, the character sheet, inventory,
drives, tutorials, and the cycle summaries. A systematic verification campaign
is riding every one of those surfaces in live play; the core loop — dice,
dialogue, the journal, the belt table, travel, and the crew systems — has been
validated, and the rest is being worked through. Notes:

- **Contracts** — the contract-stress readout and its crisis forecast were
  reworked recently and await their next full contract in live play; expect
  rough edges there first.
- **The endings** — the endgame sequences ride the ordinary table and travel
  grammar by design, but have not yet been verified end to end in live play.

## License and credits

This mod is released under the [MIT License](LICENSE). It contains no game
assets or game content.

- **Citizen Sleeper 2: Starward Vector** by
  [Jump Over The Age](https://www.jumpovertheage.com/) — created and developed
  by **Gareth Damian Martin** — published by
  [Fellow Traveller](https://www.fellowtraveller.games/). Buy the game on
  [Steam](https://store.steampowered.com/app/2442460/Citizen_Sleeper_2_Starward_Vector/).
- [BepInEx](https://github.com/BepInEx/BepInEx) by the BepInEx team
  ([license](BEPINEX_LICENSE.txt), LGPL-2.1).
- [Tolk](https://github.com/dkager/tolk) by Davy Kager
  ([license](TOLK_LICENSE.txt)).
- [NVDA](https://www.nvaccess.org/) by NV Access
  ([controller client license](NVDA_LICENSE.txt)).
