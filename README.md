# ParallelTurnPvp

Slay the Spire 2 PvP debug mod prototype built on Godot 4.5.1 Mono + .NET 9.

Current state: stable local checkpoint for two-machine debug testing.

## Current capabilities
- multiplayer debug arena entry from multiplayer menu
- two-machine direct-IP testing flow
- cards, potions, targeting, turn progression, and match end loop
- frontline-first targeting rules
- limited-intent runtime and first-pass combat overlay
- planning submission, execution-plan, and predicted-delta bridge for future delayed resolution
- structured delta-plan bridge for whitelist actions, ready for phased delayed-apply migration
- shop-draft host-authority sync skeleton (`ShopState/Request/Ack/Closed`) with debug overlay hotkeys

## Current temporary restrictions
- early-lock heal reward is temporarily disabled in live combat to avoid checksum drift
- in host-authoritative client read-only resolve mode, the client may queue and apply an authoritative live snapshot after round result delivery

## Project docs
- [执行计划](./执行计划.md)
- [架构分析与参考](./analysis/架构分析与参考.md)
- [有限意图与并行回合设计定版](./analysis/有限意图与并行回合设计定版.md)
- [稳定测试里程碑 2026-04-10](./analysis/稳定测试里程碑_2026-04-10.md)
- [CHANGELOG](./analysis/CHANGELOG.md)
- [待办清单](./analysis/待办清单.md)
- [代码结构与工作交接](./analysis/代码结构与工作交接.md)

## Build
From project root:

```powershell
.\build.ps1
```

Build plus startup smoke test:

```powershell
.\build.ps1 -SmokeTestStartup
```

## Smoke Test
Boot-level local smoke test:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\local_fastmp\Invoke-ParallelTurnStartupSmokeTest.ps1
```

This starts one local `fastmp` host and one local join client, waits for the `ParallelTurnPvp` init marker in both isolated logs, then closes the processes.

## Shop Draft Debug (Experimental)
Shop-draft debug is disabled by default in combat-engine mainline.

Optional override:

```powershell
# $env:PTPVP_ENABLE_SHOP_DRAFT="0" # force disable (default)
$env:PTPVP_ENABLE_SHOP_DRAFT="1"   # force enable
```
Restart game and enter `ParallelTurnPvP_Debug`.

Overlay hotkeys:
- `F8`: host open/close shop (debug)
- `F9`: refresh `Normal`
- `F10`: refresh `ClassBias`
- `F11`: refresh `RoleFix`
- `F12`: refresh `ArchetypeTrace`
- numpad `1..8`: purchase slot `1..8`

Input routing:
- host/singleplayer: execute locally and broadcast authoritative shop state
- client: send request to host and wait for ACK + authoritative state broadcast

## Regression
Dual-machine combat regression:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\regression\Run-DualMachineRegression.ps1
```

Shop-sync focused regression:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\regression\Run-ShopSyncRegression.ps1 -SkipBuild
```

Artifacts:
- game mods output: `mods/ParallelTurnPvp`
- portable test bundle: `torelease/mods`

## Repository layout
- `src/ParallelTurnPvp`: Godot + C# source project
- `analysis`: architecture, changelog, milestone, handoff docs
- `torelease`: portable build snapshot for secondary machine testing
