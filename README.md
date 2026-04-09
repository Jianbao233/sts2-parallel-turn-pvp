# ParallelTurnPvp

Slay the Spire 2 PvP debug mod prototype built on Godot 4.5.1 Mono + .NET 9.

Current state: stable local checkpoint for two-machine debug testing.

## Current capabilities
- multiplayer debug arena entry from multiplayer menu
- two-machine direct-IP testing flow
- cards, potions, targeting, turn progression, and match end loop
- frontline-first targeting rules
- limited-intent runtime and first-pass combat overlay

## Current temporary restrictions
- early-lock heal reward is temporarily disabled in live combat to avoid checksum drift
- PvP round result currently updates runtime state only and does not overwrite vanilla live combat state

## Project docs
- [执行计划](./执行计划.md)
- [架构分析与参考](./analysis/架构分析与参考.md)
- [有限意图与并行回合设计定版](./analysis/有限意图与并行回合设计定版.md)
- [稳定测试里程碑 2026-04-10](./analysis/稳定测试里程碑_2026-04-10.md)
- [CHANGELOG](./analysis/CHANGELOG.md)
- [代码结构与工作交接](./analysis/代码结构与工作交接.md)

## Build
From project root:

```powershell
.\build.ps1
```

Artifacts:
- game mods output: `mods/ParallelTurnPvp`
- portable test bundle: `torelease/mods`

## Repository layout
- `src/ParallelTurnPvp`: Godot + C# source project
- `analysis`: architecture, changelog, milestone, handoff docs
- `torelease`: portable build snapshot for secondary machine testing
