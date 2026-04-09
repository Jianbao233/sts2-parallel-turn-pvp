# CHANGELOG

## 2026-04-10 Stable Checkpoint
- potions now count toward intent reveal budget as well as intent slots
- `PvpRoundResolver` now emits structured round events for actions and hero/frontline deltas
- removed live combat checksum drift caused by direct `RoundResult` snapshot application
- temporarily disabled early-lock heal reward in live combat because it caused repeated checksum divergence
- stabilized two-machine debug arena loop: room entry, play card, use potion, target selection, turn end, multi-round loop, match end
- added first-pass limited-intent overlay in combat UI
- prepared repository documentation for rollback, handoff, and review

## 2026-04-09 Major Progress
- built independent `ParallelTurnPvp` Godot/.NET project and release pipeline
- added `torelease` bundle for direct copy to secondary machine
- integrated optional `DirectConnectIP` compatibility for no-Steam/IP testing
- fixed card localization, potion display, action queue poisoning, targeting, remote layout, and custom win flow
- introduced limited-intent runtime model and locked design rules
