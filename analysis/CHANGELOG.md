# CHANGELOG

## 2026-04-11 Disable Unsafe Live Delayed Apply
- disabled the first live delayed-apply cut in `PvpDelayedExecution` by feature-flagging it off
- confirmed from fresh host/client logs that mutating vanilla live combat during round switch triggers replay/checksum failures and `StateDivergence`
- kept the planning/execution/delta/prediction scaffolding intact, but restored the stable baseline where custom self-effects execute immediately again
## 2026-04-11 First Delayed Apply Cut
- introduced PvpDelayedExecution as the first real delayed-apply bridge on top of the stable instant-combat shell
- FrontlineBrace and FrontlineSalve now defer their immediate live effect in debug arena and apply it during round resolution from the current round delta plan
- this is intentionally limited to self-side custom content so the migration seam can be validated before touching vanilla defend/block-potion flow
## 2026-04-11 Delta Plan Bridge
- added PvpDeltaPlanner so RoundExecutionPlan now compiles into a structured RoundDeltaPlan
- prediction now runs from delta operations rather than directly interpreting card ids in the prediction engine
- resolver now emits delta-plan and delta-operation events, making the future delayed-apply seam explicit in logs and round summaries
## 2026-04-11 Prediction Bridge
- added PvpPredictionEngine so RoundExecutionPlan now produces a predicted end-of-round snapshot without touching vanilla live combat
- PvpRoundResult now carries PredictedFinalSnapshot alongside InitialSnapshot, FinalSnapshot, and ExecutionPlan
- resolver now emits prediction-built and prediction-compared events, making drift between planned outcomes and actual live-combat outcomes visible in logs and round summaries
- white-list prediction rules now cover the current debug content set: strike, defend, afterlife, poke, frontline brace, break formation, block potion, energy potion, blood potion, and frontline salve
## 2026-04-10 Sync Fix
- expanded the intent overlay into a combined intent + last-round summary panel so card/potion logging can be verified in-game
- reverted the experimental early-lock heal implementation that used ConsoleCmdGameAction("heal ...")
- confirmed from host/client logs that the console-command path triggers RecordInitialState must be called first on clients and corrupts replay/checksum tracking
- restored the stable baseline: the early-lock heal rule stays in design docs, but live combat execution is disabled again to avoid StateDivergence
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






## 2026-04-10 Authoritative Planning
- added PvpPlanningFrameMessage and host-authoritative planning submission broadcast
- planning frame now updates in real time after card, potion, end turn, and round start
- clients now consume authoritative planning frames instead of relying only on local round reconstruction
- intent view now prefers authoritative planning frame data when available

- fixed client-side intent overlay not updating after same-round actions by preventing same-round planning-frame rebuilds and removing overly strict snapshot-version gating

- moved round summary generation onto planning submissions so resolver input now follows the planning layer instead of raw action logs

- added PvpExecutionPlanner and RoundExecutionPlan so resolver now groups planned actions into execution phases before generating summaries

- added secondary-machine workflow: optional direct deploy to \\DESKTOP-U51KJJ2\Mods and direct log pull from \\DESKTOP-U51KJJ2\SlayTheSpire2\logs




