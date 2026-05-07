# CHANGELOG

## 2026-05-07 - Shared Combat Reconnect Resume Gate Fix
- Fixed multiple reconnect-time shared-combat gates that were still split-room-only by mistake:
  - `PumpClientResumeStateRequest(...)`
  - `PumpClientPlanningFrameResync(...)`
  - client round-state live snapshot apply gate
  - deferred planning-frame buffering gate
  - `TryAlignCombatRoundNumber(...)`
- Result:
  - shared-combat running-session rejoin can now actually send `ResumeStateRequest`,
  - authoritative round/snapshot/planning state can be reapplied after reconnect,
  - client live combat round-number alignment now runs in the same host-authoritative snapshot-sync mode instead of silently no-oping.
- Added an immediate resume-request kick after `DirectConnectIP` running-session rejoin bootstrap marks the runtime as `disconnected-pending-resume`, reducing dependence on overlay refresh timing for the first recovery packet.


## 2026-04-28 - E3 Alternating Combat Order + Damage Timing Fix
- Changed `PvpExecutionPlanner` ordering from live `RuntimeActionId` order to PvP round action order:
  - non-EndTurn actions resolve by action sequence first,
  - players with the same sequence are interleaved deterministically,
  - the first finisher is used as the first-side tie breaker,
  - EndTurn markers are always processed after card/potion actions.
- Purpose:
  - make round resolution match the locked design (`双方出牌顺序交错合并`),
  - prevent a later `Defend` action from applying before the opponent's earlier attack just because its live action id is smaller.
- Log diagnosis:
  - host logs showed `DelayedCommand` applying `DEFEND_NECROBINDER` before a cross-player attack even though the round summary grouped attack before buff.`r`n`r`n## 2026-04-22 - E1 Step1 Mainline Switch: SharedCombat Default
- Switched room-topology default from split-room shell to shared-combat:
  - PTPVP_ENABLE_SPLIT_ROOM unset => now defaults to SharedCombat.
- Kept split-room as optional test mode via env override:
  - enable: PTPVP_ENABLE_SPLIT_ROOM=1/true/on/yes
  - disable: PTPVP_ENABLE_SPLIT_ROOM=0/false/off/no
- Goal:
  - align with current plan baseline (同场 + Host 权威交错结算),
  - reduce turn-progression complexity while finishing Step1 stability work.

## 2026-04-22 - E2 Step2 Targeting: Hero + Frontline Both Selectable (SharedCombat)
- Updated enemy target enumeration in shared-combat mode:
  - when enemy frontline exists, target list now contains both EnemyFrontline and EnemyHero.
- Updated enemy target validation:
  - enemy hero is now a valid explicit target even if frontline is alive.
- Note:
  - this patch changes only target selection gate;
  - interception/overflow behavior remains resolver-side.

## 2026-04-18 - D4.11 EndTurn LiveRound Compatibility (Turn-Progress Unblock)
- Fixed split-room client EndTurn round-alignment to satisfy vanilla execute guard:
  - prefer `max(authoritativeRound, liveRound)` for EndTurn action round,
  - avoid hard drop pattern `Ignoring end turn action (current > action round)`.
- Applied to both request-enqueue and execute-prefix alignment paths.
- Result:
  - resolved turn-stuck regression introduced by over-constraining EndTurn round to authoritative-only value.

## 2026-04-18 - D4.10 Client EndTurn Round Drift Guard (Reconnect)
- Hardened EndTurn round-alignment in split-room client host-authority mode:
  - prefer authoritative `runtime.CurrentRound.RoundIndex` over local `liveRound`,
  - prevent forward bump (`actionRound < targetRound`) that can push client local combat round ahead.
- Applied to both request-enqueue path and execute-prefix path.
- Purpose:
  - reduce reconnect-time local round drift (e.g. `from=6 to=7`),
  - avoid client-side turn progression artifacts that look like state desync.

## 2026-04-18 - D4.9 Client Planning Resync Pump (ACKed-Frame Gap Self-Heal)
- Added client-side planning resync pump:
  - when client has ACKed submission revision R for current (round,snapshot),
  - but authoritative planning frame has not caught up to R within grace window,
  - client sends throttled ResumeStateRequest as a lightweight resync hint request.
- Added rate limiting and probe tracking:
  - grace window to avoid transient packet reordering noise,
  - periodic resend interval to avoid request storms.
- Purpose:
  - reduce rare ACK-arrived-but-frame-lag stalls,
  - improve automatic recovery without entering full disconnect-resume flow.

## 2026-04-18 - D4.8 Client Planning ACK-Floor + Duplicate Submit Send Guard
- Added client-side ACK-floor guard for authoritative planning frames:
  - if a received/deferred planning frame revision is lower than the local ACKed revision (same round/snapshot), it is ignored/dropped.
- Added duplicate immediate submit-send suppression:
  - same `(round,snapshot,revision,actionCount,locked)` submission sent again within a short window is skipped.
- Purpose:
  - prevent out-of-order older planning frames from rolling client planning state backward after ACK,
  - reduce duplicate send noise and unnecessary `duplicate_revision` ACK churn.

## 2026-04-18 - D4.7 Deferred PlanningFrame Consume-Order Fix
- Fixed a host/client race where authoritative `PlanningFrame` could be dropped permanently:
  - previous flow marked frame as `received` before client-side applicability checks,
  - when round-state/snapshot was not ready, the frame was deferred but already consumed by dedupe gates.
- New flow:
  - defer path now caches frame first (without advancing received marker),
  - dedupe marker is advanced only when frame is actually applied,
  - after authoritative `RoundState` apply, client now attempts deferred-frame replay immediately.
- Added telemetry for:
  - deferred frame cached / ignored(older),
  - deferred frame applied,
  - deferred frame dropped as stale.
- Expected impact:
  - fewer `round_mismatch`/`snapshot_mismatch` cascades after packet reordering,
  - reduced turn-stall cases where both sides look locked but host never sees aligned client planning state.
## 2026-04-18 - D4.6 Submission Noise Throttle (Host Log)
- Added per-run/per-round submission noise throttle for host pre-gate logs:
  - `stale_before_record`
  - `duplicate_revision`
- Throttle key is `(tag, senderPlayerId, revision)` and resets on round change.
- Purpose:
  - keep battle debugging logs readable under packet replay/reorder scenarios,
  - preserve first-hit diagnostics while suppressing repetitive duplicates.

## 2026-04-18 - D4.5 Submission Revision Pre-Gate (Host)
- Added host-side revision pre-gate before `RecordNetworkSubmission(...)`:
  - `incomingRevision < knownRevision` -> ACK `already_applied` and ignore early.
  - `incomingRevision == knownRevision` -> ACK `duplicate_revision` and ignore early.
- Purpose:
  - suppress stale/conflicting same-revision noise in host logs,
  - avoid unnecessary validation path work on already-covered submissions.

## 2026-04-18 - D4.4 Submission NACK Retry Hardening
- Added host pre-check NACK reasons before `RecordNetworkSubmission(...)`:
  - `sender_mismatch`
  - `round_mismatch`
  - `snapshot_mismatch`
- Added client-side NACK policy:
  - only `note=rejected` triggers immediate retry,
  - non-retryable NACK now stops aggressive immediate resend loop for the current submission window.
- Purpose:
  - prevent retry storms when host/client context is already known-mismatched,
  - keep logs actionable and reduce unnecessary message churn.

## 2026-04-18 - D4.3 Host Submission Bootstrap (Round-0 Reject Spam Fix)
- Added host-side round-context bootstrap on incoming `PvpClientSubmissionMessage`:
  - when host runtime is still `round=0` / `snapshot=0`, it now initializes from live combat state before validating submission payload.
- Added explicit reject ACK note `round_not_ready` when combat context is unavailable.
- Added bootstrap telemetry:
  - successful first-packet bootstrap log with round/snapshot,
  - pre-check failure diagnostics when combat context is missing.
- Purpose:
  - eliminate massive `Rejected network submission: round mismatch ... localRound=0` spam on early client actions,
  - reduce first-round submission loss risk under deferred host round init.

## 2026-04-18 - D4.2 Combat-Focused Line: Shop Debug Default-Off
- Switched `PvpShopFeatureFlags` default to OFF when `PTPVP_ENABLE_SHOP_DRAFT` is unset.
- Guarded shop net-bridge registration behind feature flag:
  - `ParallelTurnPvpMod.Initialize()`
  - `PvpNetBridge.EnsureRegistered()`
- Removed `shopDraftEnabled` from init summary log to keep combat debug logs cleaner.
- Result:
  - no shop debug overlay/hotkeys/messages on default combat test line,
  - shop debug can still be explicitly enabled with `PTPVP_ENABLE_SHOP_DRAFT=1`.

## 2026-04-18 - D4.1 Combat Sync Hardening: Stale Round Payload Guard
- Added stale-round guards in `PvpNetBridge` for:
  - `PvpRoundStateMessage`
  - `PvpPlanningFrameMessage`
  - `PvpRoundResultMessage`
- New behavior:
  - when incoming `roundIndex < runtime.CurrentRound.RoundIndex`, payload is now ignored with structured warning logs instead of being applied.
- Goal:
  - prevent out-of-order/late network packets from rolling runtime state back to older rounds,
  - reduce turn-stall and desync cases caused by stale authoritative payloads.

## 2026-04-16 - D3.9 Shop Draft Feature Flag Default-On (F8/F9 Debug Hotkeys)
- Changed `PvpShopFeatureFlags` default behavior: when `PTPVP_ENABLE_SHOP_DRAFT` is unset, shop-draft debug is now enabled by default.
- Kept explicit env override support:
  - `PTPVP_ENABLE_SHOP_DRAFT=0` -> force disable
  - `PTPVP_ENABLE_SHOP_DRAFT=1/true` -> force enable
- Purpose: avoid silent `F8/F9/F10/F11/F12` no-op in routine PvP debug sessions when env var is not preconfigured.

## 2026-04-16 - D3.8 Shop Debug Input/Overlay + ShopSync Regression Script
- Added practical in-combat shop debug entry in `ParallelTurnIntentOverlay` (shop-draft enabled line):
  - real-time shop section rendering (open state / round / snapshot / state version / gold / refresh costs / offers / status),
  - action status line for latest local debug command result.
- Added shop debug hotkeys:
  - `F8`: host open/close shop round,
  - `F9/F10/F11/F12`: trigger `Normal/ClassBias/RoleFix/ArchetypeTrace` refresh,
  - numpad `1..8`: purchase slot `1..8`.
- Added host/client split behavior for debug actions:
  - host executes local `TryRefresh/TryPurchase` and broadcasts authoritative shop state,
  - client sends request messages (`TrySendRefreshRequest/TrySendPurchaseRequest`) and relies on ACK + state broadcast.
- Added shop-sync focused regression script:
  - `tools/regression/Run-ShopSyncRegression.ps1`
  - validates `open/state/request/ack/close` chain and emits markdown report under `analysis/regression_reports/shopsync/*`.
- Updated `README.md` with shop debug enable flag, hotkeys, and regression command docs.

## 2026-04-16 - D3.7 EndTurn Round-Mismatch Hardening (Reconnect Turn-Stall)
- Root-cause signal from dual logs:
  - `Ignoring end turn action. Current round number: 3 action round number: 1`
  - mirrored on peer as `Current round number: 1 action round number: 3`.
- Hardened `TrackEndTurnPatch` round alignment:
  - no longer depends only on fixed member names (`RoundNumber/_roundNumber/...`).
  - now scans all int round-related members (properties/fields containing `round`) and applies round rewrite in prefix.
  - if direct read fails, still attempts write-path alignment using reflective round-member set.
- Added one-shot diagnostics for unresolved reflection layouts:
  - logs discovered end-turn round-related property/field names once when alignment cannot be applied.
- Bumped `ProtocolVersion/ContentVersion` to `35`.

## 2026-04-16 - D3.6 Rejoin Round Authority Hard-Reset (Turn Stuck Hotfix)
- Fixed rejoin round ownership model to be truly host-authoritative on client resume:
  - removed `Math.Max(localRound, hostRound)` behavior in resume apply path.
  - resume now hard-resets current round metadata to host `RoundState` (round/phase/snapshot/revision).
- Added round-state rehydrate flow for resume:
  - clears stale local round caches (`logs/intents/dedupe/networkSubmissions/revision`).
  - rebuilds per-player planning state from authoritative `PlanningFrame` submissions when snapshot matches.
  - prevents stale pre-disconnect lock/actions from polluting post-reconnect turn progression.
- Hardened planning-frame authority apply:
  - `ApplyAuthoritativePlanningFrame` now always aligns to incoming frame round/snapshot/phase/revision (instead of only advancing upward).
- Safety gate for live round number mutation:
  - `TryAlignCombatRoundNumber` now only mutates `CombatState.RoundNumber` when `PTPVP_ENABLE_ROUND_NUMBER_MUTATION=1`.
  - default behavior no longer rewrites vanilla live round counter during resume sync.
- Bumped `ProtocolVersion/ContentVersion` to `34`.

## 2026-04-16 - D3.5 Rejoin Snapshot/Round Alignment Fix
- Extended `PvpRoundStateMessage` to carry authoritative round-start snapshot payload:
  - hero/frontline `hp/maxHp/block/exists` now shipped with round-state sync.
- Updated round-state apply flow:
  - `HandleRoundStateMessage` now rebuilds `SnapshotAtRoundStart` from authoritative payload instead of local live snapshot only.
  - client now applies authoritative round-start snapshot to live combat immediately in split-room mode.
- Updated resume flow:
  - `HandleResumeStateMessage` round-state path now restores full authoritative snapshot (not metadata-only clone).
  - resume now attempts to align client live `CombatState.RoundNumber` to authoritative round index.
  - stale resume round-result (`resultRound < stateRound`) is ignored to avoid polluting current round phase.
- Updated resume response build:
  - round-state uses authoritative snapshot payload builder.
  - stale historical round-result is no longer sent when host is already in a newer round.
- Added `EndPlayerTurnAction` pre-execute round alignment fallback patch to avoid round mismatch drops after reconnect (`action round != live round`).
- Bumped `ProtocolVersion/ContentVersion` to `33`.

## 2026-04-16 - D3.4.1 ForceSwitch Kick Safety Gate (Hotfix)
- Root cause from normal-match stall:
  - aggressive host fallback kick invoked `SwitchSides` during vanilla `EndPlayerTurnPhaseTwo`, triggering:
    - `InvalidOperationException: EndPlayerTurnPhaseTwo called while the current side is Enemy`.
- Hotfix:
  - added env gate `PTPVP_ENABLE_FORCE_SWITCH_KICK` (default OFF).
  - `PumpHostResolveFallback(...)` and `PumpRoundAlignment(...)` now no-op unless env is explicitly set to `1`.
  - removed eager host-fallback pump call from `TrackEndTurnPatch`.
- Result target:
  - restore stable normal round progression first; reconnect force-kick remains opt-in for debugging only.

## 2026-04-16 - D3.4 Rejoin Round Progress Kick & Alignment Fix
- Added split-room round-alignment pump:
  - `PvpNetBridge.PumpRoundAlignment(...)` now detects `liveRound < pvpRound` after rejoin and triggers a guarded `SwitchSides` catch-up kick.
  - wired into `ParallelTurnIntentOverlay` refresh loop for continuous recovery.
- Added host resolve fallback pump:
  - `PvpNetBridge.PumpHostResolveFallback(...)` now detects `CanResolveRound == true` but unresolved player phase and triggers a guarded `SwitchSides` kick.
  - invoked both from overlay refresh and after accepted client submission/end-turn tracking.
- Added guarded `ForceSwitchSidesAsync(...)` trampoline with per-source checks and throttle logs (`round_align` / `host_resolve_fallback`).
- Added split-room null-guard for `NMultiplayerPlayerIntentHandler._Process` to suppress reconnect-time remote cursor NRE spam.
- Fixed checksum bypass patch signature to match current `CompareChecksums` argument shape so host-side bypass can actually hook.
- Bumped `ProtocolVersion/ContentVersion` to `31`.

## 2026-04-15 - D3.3 Rejoin RoundState Snapshot Restore Fix (Stuck After Both EndTurn)
- Fixed resume-state apply path to restore missing round snapshot context on client:
  - `HandleResumeStateMessage` now restores:
    - `CurrentRound.RoomSessionId / RoomTopology`
    - `CurrentRound.SnapshotAtRoundStart.RoundIndex / SnapshotVersion`
    - `TryMarkRoundStateReceived(...)` marker alignment
- Hardened planning-frame apply path:
  - `ApplyAuthoritativePlanningFrame(...)` now updates:
    - `CurrentRound.SnapshotAtRoundStart.SnapshotVersion` (when newer frame arrives)
    - `CurrentRound.PlanningRevision`
    - room session context fields
- Added explicit warning when client submission is skipped due invalid planning frame fields.
- Root cause addressed:
  - after running rejoin, client could keep `snapshotVersion=0`, causing submission send guard to drop packets;
  - host then stayed waiting at `LockedWaitingPeer` after both sides appeared to end turn.
- Bumped `ProtocolVersion/ContentVersion` to `30`.

## 2026-04-15 - D3.2 Rejoin Desync Hotfix (Vanilla Checksum Bypass for SplitRoom)
- Added PvP-only checksum bypass patches under split-room debug modifier:
  - suppress `ChecksumTracker.CompareChecksums(...)` mismatch path,
  - suppress `ChecksumTracker.OnReceivedStateDivergenceMessage(...)` handling path.
- Scope guard:
  - only active when current run contains `ParallelTurnPvpDebugModifier` and split-room mode is enabled.
  - does not affect non-PvP/non-split-room runs.
- Rationale:
  - split-room PvP uses `ParallelTurn` host-authoritative runtime as the correctness source,
  - vanilla combat checksum parity is no longer a valid source of truth after running-session rejoin.
- Bumped `ProtocolVersion/ContentVersion` to `29`.

## 2026-04-15 - D3.1 Running Rejoin Hotfix (Neow/BlackScreen Path)
- Fixed running-session rejoin local identity resolution:
  - `PvpRoomSessionFactory` now prefers `RunManager.Instance.NetService.NetId` over `LocalContext.GetMe(...)`.
  - avoids client-side `RoomSession.LocalPlayerId` resolving to host id during rejoin timing windows.
- Hardened running-session rejoin bootstrap path:
  - keeps vanilla `CombatStateSynchronizer` disabled on the running rejoin route (ParallelTurn uses its own PvP authority sync),
  - logs post-load room type for diagnostics.
- Added rejoin Neow-combat bridge:
  - when running rejoin lands in `Neow` event room, auto-enters debug arena combat without requiring manual Neow click,
  - reduces “rejoin -> Neow option -> black screen” failure path.

## 2026-04-15 - D3 Running Rejoin Compatibility (DirectConnectIP Path)
- Added DirectConnectIP compatibility patch:
  - runtime patches `DirectConnectIP.Network.ConnectionService.RouteSessionState`.
  - for `RunSessionState.Running` + `ParallelTurnPvP` run, bypasses DirectConnectIP's hard `RunInProgress` disconnect path.
- Added client-side running rejoin bootstrap:
  - rebuilds run from `ClientRejoinResponseMessage.serializableRun`,
  - sets up `LoadRunLobby + RunManager.SetUpSavedMultiPlayer`,
  - loads run scene directly (`NGame.LoadRun`) and keeps session connected,
  - marks runtime `DisconnectedPendingResume` and re-enters host-resume pull path.
- Added guard to avoid duplicate running rejoin bootstraps.
- Added safety guard: `PumpClientResumeStateRequest` now skips sends when client net service is not connected.
- Bumped `ProtocolVersion/ContentVersion` to `28`.

## 2026-04-15 - D2 Skeleton: Resume Request/Response Channel
- Added reconnect-resume protocol skeleton:
  - `PvpResumeStateRequestMessage`
  - `PvpResumeStateMessage`
- Client behavior:
  - when runtime enters `DisconnectedPendingResume`, client now periodically requests resume state from host.
- Host behavior:
  - responds with best-known authoritative bundle for the same room context:
    - current `RoundState` (if available)
    - latest `PlanningFrame` (authoritative or compiled fallback)
    - latest `RoundResult` (if available)
  - on valid opponent resume request, host clears local disconnected freeze state.
- Client apply path:
  - accepts resume bundle only in disconnected-pending-resume state
  - applies planning/result snapshots and then clears disconnected freeze state.
- Bumped `ProtocolVersion/ContentVersion` to `27`.

## 2026-04-15 - Disconnect Popup Role Fix (Host/Client wording)
- Fixed reversed disconnect wording by removing generic `NErrorPopup.Create(info)` mapping.
- Popup text is now generated from disconnect source:
  - client side: `与主机断开连接`
  - host side peer drop: `客机断开连接`
  - host self drop: `主机网络中断`
- Keeps native modal popup style while ensuring role-correct messaging.

## 2026-04-15 - Disconnect UX Switch: Native Modal Popup
- Replaced disconnect red-float notice with native game modal popup:
  - now uses `NErrorPopup` + `NModalContainer` on first disconnect transition.
- Removed custom system float-text notice code path to avoid non-native overlays.
- Kept right-panel disconnected state text as secondary status context.

## 2026-04-15 - Disconnect UX Hotfix: Guaranteed Visible Prompt
- Fixed disconnect prompt visibility gap in combat:
  - intent panel no longer auto-hides when `view == null` if runtime is in disconnected-pending-resume.
  - added explicit disconnected fallback body text for overlay.
- Added one-shot combat notice float when entering disconnected state:
  - `联机中断：<reason>（远端=<id>）`.
- Goal: ensure testers can immediately see "why local actions are frozen" without relying on logs.

## 2026-04-15 - D1 Groundwork: Disconnect Detection + Local Action Freeze
- Added runtime disconnect state to PvP core:
  - `PvpMatchRuntime.IsDisconnectedPendingResume`
  - `PvpMatchRuntime.DisconnectReason`
  - `MarkDisconnectedPendingResume(...)` / `ClearDisconnectedPendingResume(...)`
- Hooked multiplayer disconnect callbacks with Harmony:
  - `NetClientGameService.OnDisconnectedFromHost`
  - `NetHostGameService.OnPeerDisconnected`
  - `NetHostGameService.OnDisconnected`
- On active debug-arena match disconnect:
  - runtime enters `disconnected pending resume`,
  - current phase is held in waiting state,
  - local gameplay action enqueue is frozen (`PlayCard` / `UsePotion` / `EndTurn` / `UndoEndTurn`).
- Intent overlay now displays disconnect status and freeze hint in Chinese.
- Client submission retry pump now pauses while disconnected-pending-resume.
- Added `PvpRuntimeRegistry.TryGet(RunState)` helper for disconnect patches.
- Bumped `ProtocolVersion/ContentVersion` to `26`.

## 2026-04-15 - Host Submission Payload Validation Guard
- Added host-side structural validation for incoming client submissions before acceptance:
  - max action count guard,
  - contiguous sequence validation (`0..n-1`),
  - duplicate `RuntimeActionId` rejection within submission,
  - action type whitelist (`PlayCard`, `UsePotion`, `EndRound`),
  - model entry non-empty check,
  - `EndRound` uniqueness + must be last + must target `None`,
  - locked/unlocked semantic consistency (`locked => has EndRound`, `unlocked => no EndRound`),
  - target owner must be a known player id.
- Invalid payloads are now rejected with explicit reason:
  - `Rejected network submission: invalid payload ... reason=...`
- Regression scanner now tracks `InvalidSubmissionPayload`.
- Bumped `ProtocolVersion/ContentVersion` to `25`.

## 2026-04-15 - Strict Resolve Telemetry + Regression WARN Grade
- Added round-level resolve source/degradation telemetry:
  - `PvpRoundState.ResolveInputSourceTag`
  - summary fields `resolveInputSource`, `resolverDegraded`
- Host round summary now records both strict-degradation dimensions:
  - `resolverFallbackCount/Players` (synthesized remote submission)
  - `resolverForcedLockedCount/Players` (remote submission forced to locked)
- `ResolveLiveRound` logs now include `source=... forcedLocked=... synthesized=...`.
- Updated dual-machine regression script:
  - detects strict mode signals (`ResolverForcedLockUsed`),
  - parses new summary fields,
  - outputs `PASS/WARN/FAIL` where strict-degraded rounds map to `WARN` (and divergence/parity mismatch remain `FAIL`).
- Bumped `ProtocolVersion/ContentVersion` to `24`.

## 2026-04-15 - Resolve Input Source Priority + Strict Fallback Telemetry
- `ResolveLiveRound` now prefers `authoritative_planning_frame` as its resolve input source when available (same-round); falls back to `local_compiled_logs` only when authoritative frame is unavailable.
- Added strict fallback observability:
  - track `ResolverForcedLockedPlayers` (remote submission arrived but unlocked, forced to locked at resolve boundary),
  - keep `ResolverFallbackPlayers` (synthesized locked submission for missing remote submission),
  - emit round events for both conditions when they occur.
- Host round summary NDJSON now includes:
  - `resolverForcedLockedCount`
  - `resolverForcedLockedPlayers`
- Resolve summary log now prints `source=... forcedLocked=... synthesized=...`.
- Bumped `ProtocolVersion/ContentVersion` to `23`.

## 2026-04-15 - SplitRoom Resolver Strict Fallback (No Compiled-Log Fallback)
- Updated host split-room resolver input merge policy:
  - remote player submissions now come from network submission channel only;
  - if remote submission exists but is unlocked at resolve boundary, resolver forces it to locked and keeps known actions;
  - if remote submission is missing at timeout, resolver synthesizes an empty locked submission for that remote player.
- Removed previous `fallback=compiled_log` behavior from resolve-time timeout path; timeout log now reports `fallback=network_strict`.
- Purpose: remove remaining dependency on same-room compiled action side effects during host resolve and reduce divergence surface.
- Bumped `ProtocolVersion/ContentVersion` to `22`.

## 2026-04-15 - SplitRoom Host Live-Tracking Strict Mode (Submission-First)
- In `SplitRoom + Host`, live runtime action tracking now ignores remote actor actions (`PlayCard`, `UsePotion`, `EndTurn`) and relies on remote submission channel to write authoritative action logs.
- Added explicit logs:
  - `SplitRoom host ignored live ... from remote actor ...; relying on submission channel.`
- Purpose: further reduce dependency on same-room action side effects and move toward B4.1 formal submission-first resolution.
- Bumped `ProtocolVersion/ContentVersion` to `21`.

## 2026-04-15 - Heal VFX Fallback for Secondary Visibility
- Improved delayed-heal number presentation: when native `NHealNumVfx` creation fails, fallback to a green floating text (`+N 治疗`) instead of silently skipping.
- This keeps heal feedback visible on secondary/client even when native VFX node creation is unavailable for that frame/context.
- Bumped `ProtocolVersion/ContentVersion` to `20`.

## 2026-04-15 - Delayed Apply Submission Source Alignment
- `PvpDelayedExecution.ApplyDelayedLiveEffects` now consumes `runtime.GetResolverSubmissions()` instead of local `GetPlanningSubmissions()`.
- This aligns delayed live apply with resolver input source (authoritative planning frame / network-merged submissions), reducing pre-resolve host/client drift risk in split-room mode.
- Added delayed apply log marker `source=resolver_submissions` for easier dual-log verification.
- Bumped `ProtocolVersion/ContentVersion` to `19`.

## 2026-04-15 - Hotfix: Client Read-Only Resolve Default Rollback
- Rolled back `PTPVP_ENABLE_CLIENT_READONLY_RESOLVE` default from `ON` to `OFF` to restore stable baseline while keeping telemetry instrumentation.
- Bumped `ProtocolVersion/ContentVersion` to `18` to prevent mixed-version sessions after rollback.

## 2026-04-15 - B6 Default-On + Authoritative Wait Telemetry
- Switched `PTPVP_ENABLE_CLIENT_READONLY_RESOLVE` default to `ON` (still overridable with `0/false`).
- Client read-only resolve path now binds to runtime modifier config and records wait telemetry:
  - `Client waiting authoritative round result...`
  - periodic `still waiting` progress logs
  - threshold warning when authoritative result wait exceeds `4500ms`
  - clear log when authoritative result is received
- Hooked authoritative result handler to clear client wait tracker on accepted host round result.
- Bumped `ProtocolVersion/ContentVersion` to `17`.

## 2026-04-15 - Read-Only Resolve Runtime Binding
- Updated runtime decision for client read-only resolve to prefer the current run's modifier field (`ClientReadOnlyResolveEnabledField`) rather than relying only on local env var.
- This keeps in-combat behavior aligned with the lobby-locked match config and avoids mode drift across sessions.
- Deployed to both host and secondary after build verification.

## 2026-04-15 - Client Submission Retry Tuning
- Tuned client submission retry strategy after lock for split-room reliability:
  - retry cadence is now staged (`300ms` fast, `500ms` normal, `800ms` slow)
  - max retries per round increased from `8` to `12`
  - added one-shot `retry exhausted` warning when retry budget is consumed
- NACK-triggered immediate resend path now resets retry exhaustion state and uses fast interval backoff.
- Goal: reduce host-side early fallback probability in high jitter windows while keeping retry spam bounded.

## 2026-04-14 - Adaptive Resolve Grace (Host)
- Improved host-side resolve wait strategy in split-room mode to reduce premature fallback under jitter:
  - base timeout remains `1800ms`
  - +`900ms` if recent network submission activity is detected
  - +`1200ms` if missing players are already locked in local compiled logs
- Added timeout reason telemetry to waiting/timeout logs (`base`, `recent_network_activity`, `missing_peer_locked_local_log`).
- Host now records last accepted network submission timestamp per round runtime and resets it on round start.
- Built and deployed to host + secondary (`build.ps1 -DeploySecondary`).

## 2026-04-14 - Version Guard Extended for Read-Only Resolve Mode
- Added `ClientReadOnlyResolveEnabledField` to `ParallelTurnPvpDebugModifier` saved state so lobby metadata carries this runtime mode explicitly.
- Extended debug lobby mismatch guard to compare and display read-only resolve mode (`/Oon` / `/Ooff`) in the host/local signature.
- Custom run screen debug log now prints `clientReadOnlyResolve=...` for quick mode verification.
- Bumped `ProtocolVersion/ContentVersion` to `16`.

## 2026-04-14 - B6 Gray Switch: Client Read-Only Resolve
- Added `PvpResolveConfig` with env flag `PTPVP_ENABLE_CLIENT_READONLY_RESOLVE` (default `OFF`).
- In split-room mode, when this flag is enabled on client, `SwitchSides` resolve point now skips local `ResolveLiveRound` and waits for host authoritative `RoundResult`.
- Added startup config log field `clientReadOnlyResolve=...` to verify runtime mode quickly.
- This is a gray-release path toward B6 without changing current default behavior.

## 2026-04-14 - StartTurn Tail Exception Guard
- From latest dual-machine logs, host still hit `CombatManager.StartTurn` nullable exception after combat had already transitioned to `NotInCombat`.
- Hardened `ParallelTurnSuppressMatchEndStartTurnErrorPatch`:
  - added shared debug-arena run detection helper
  - kept post-match `StartTurn` short-circuit behavior
  - broadened finalizer suppression for `InvalidOperationException: Nullable object must have a value.` to debug arena scope (logs `matchEnded` state)
- Rebuilt and redeployed to host + secondary via `build.ps1 -DeploySecondary`.

## 2026-04-14 - Submission ACK/NACK Reliability
- Completed independent `PvpClientSubmissionAckMessage` wiring in `PvpNetBridge` so host explicitly returns ACK/NACK for client submission packets.
- Fixed rejected-submission ACK revision floor to avoid `revision=0` drops and keep client retry state progressing.
- Added client-side immediate resend on NACK for same `round/snapshot`, reducing lock-wait stalls before resolver fallback.
- Build+deploy executed with `build.ps1 -DeploySecondary`; artifacts synced to both machines and `torelease`.

## 2026-04-14 - B4.1 Progress: Submission Retry + Resolve Grace Timeout
- Added client submission retry pump (`PvpNetBridge.PumpClientSubmissionRetry`) for split-room mode:
  - when local player already locked, auto-retry submission on interval (`700ms`, max `8` retries/round)
  - retry state is tracked per run (`round/snapshot/revision/player/locked/actionCount/lastSentUtc/retryCount`)
- Wired retry pump into overlay refresh loop so retries continue while waiting peer lock/resolve window.
- Host now broadcasts latest authoritative planning frame immediately after accepting a client submission (lightweight ACK path).
- Client retry pump now suppresses further retries once authoritative planning frame already contains local locked submission with matching/newer revision.
- Added host-side resolve grace in `PvpMatchRuntime.CanResolveRound`:
  - in split-room host mode, resolve now waits briefly for missing remote network submissions (`1800ms`)
  - before timeout: block resolve and log wait progress
  - after timeout: allow fallback resolve with explicit warning (`fallback=compiled_log`)
- Regression scanner now includes `ResolveWaitTimeoutFallback` signal for this path.
- Goal: reduce race windows where host resolves too early and reduce fallback-trigger frequency in unstable network timing.
- Bumped `ProtocolVersion/ContentVersion` to `15`.

## 2026-04-14 - Regression Tooling: Round Summary Parity Compare
- Enhanced `tools/regression/Run-DualMachineRegression.ps1` to ingest host/secondary `ptpvp_round_summary.ndjson` tails and compare common rounds.
- New report section `RoundSummaryParity` includes:
  - summary file presence
  - compared round count
  - parity mismatch samples (snapshot/delayed signature)
  - fallback round samples (`resolverFallbackCount`)
- Overall regression status now treats summary parity mismatch as `FAIL`.

## 2026-04-14 - Hotfix: Disable Shop Draft Bridge by Default (Desync ID12)
- Investigated latest dual log desync: both peers diverged at checksum `ID 12` after synchronized `ConsoleCmdGameAction` command:
  - `Executing DevConsole command (player ...): \`card STRIKE_NECROBINDER\``
- Added `PvpShopFeatureFlags` with env toggle `PTPVP_ENABLE_SHOP_DRAFT` and defaulted shop-draft bridge to OFF for stability.
- `PvpShopBridgeSwitchSidesPatch` now no-ops unless `PTPVP_ENABLE_SHOP_DRAFT=1`.
- Added startup log flag `shopDraftEnabled=...`.
- Regression scanner now tracks `CardConsoleCmdInCombat`.
- Bumped `ProtocolVersion/ContentVersion` to `13`.

## 2026-04-14 - B4.1 Telemetry: Resolver Fallback Visibility
- Added round-level resolver fallback tracking in runtime (`ResolverFallbackPlayers`), including per-round warning summary when fallback path is used.
- Host round summary NDJSON now includes:
  - `resolverFallbackCount`
  - `resolverFallbackPlayers`
- Regression script `tools/regression/Run-DualMachineRegression.ps1` now scans fallback markers (`ResolverFallbackUsed`) so split-room fallback usage is visible in each report.

## 2026-04-14 - B4.1 Progress: Client Submission Revision Monotonic Guard
- Extended `PvpClientSubmissionMessage` payload with `revision` (planning frame revision at send time).
- Host now validates per-player submission revision monotonicity:
  - rejects stale revisions
  - rejects conflicting payloads on same revision
  - keeps duplicate same-revision/same-payload as no-op
- Purpose: reduce split-room race noise from out-of-order/replayed client submissions and stabilize authority input stream.
- Bumped `ProtocolVersion/ContentVersion` to `12` for packet layout compatibility.

## 2026-04-14 - B4.1 Progress: Incremental Client Submission Stream
- Client submission channel now sends incremental snapshots not only at `EndTurn`, but also after each tracked `PlayCard` and `UsePotion` action (client local actor only).
- Goal: reduce race windows where host resolves a round before receiving the latest client-side submission state.
- Existing duplicate suppression remains on host (`Ignored duplicate network submission`).

## 2026-04-14 - B4.1 Progress: Host Log/Intent Sync from Client Submission
- Host now projects accepted `PvpRoundSubmission` back into runtime action log (`LogsByPlayer`) and public intent slots for that player.
- This reduces dependence on same-room side-effect action capture and keeps host planning/intents aligned to client submitted sequence.
- Existing fallback behavior remains unchanged (still allows compiled fallback when submission missing), but default path is now closer to pure submission authority.

## 2026-04-14 - Automation A1: Dual-Machine Regression Script
- Added `tools/regression/Run-DualMachineRegression.ps1`.
- Script capability:
  - optional build + optional secondary deploy
  - host/secondary `godot.log` tail extraction
  - keyword scans (`StateDivergence`, snapshot mismatch, init-order, nullable crash, protocol/room mismatch, hard exceptions)
  - markdown report output under `analysis/regression_reports/<timestamp>/report.md`
- Purpose: shorten manual triage loop and keep comparable evidence for each test round.

## 2026-04-14 - Host Round Summary Sink (C2)
- Added host-only round summary persistence: each resolved round appends one NDJSON record to:
  `%AppData%\\SlayTheSpire2\\logs\\ptpvp_round_summary.ndjson`.
- Summary includes: session/topology, round/snapshot versions, per-player submission action list, final hero/frontline states, delayed fingerprint/count, event count.
- Purpose: provide deterministic post-mortem evidence for desync analysis and rollback comparison.

## 2026-04-14 - Rollback: Client Read-Only Resolve Path (stability first)
- Rolled back the client-side "skip local resolve" branch after repeated `checksum ID 11` divergence in split-room tests.
- Client now follows the same local delayed-apply + round resolve path as host again (no early return in `SwitchSides` prefix).
- Authoritative round-result snapshot handling on client is reverted to queued apply mode (`QueueAuthoritativeSnapshot`), removing immediate live apply side effects from message receive timing.
- Kept the snapshot-version alignment fix from previous step.
- Bumped `ProtocolVersion/ContentVersion` to `11`.

## 2026-04-14 - Snapshot Version Alignment Fix (Round2 submission rejection)
- Root cause found from dual logs: host rejected client round-2 submission with `incomingSnapshot=2 localSnapshot=3`, then resolved with incomplete mixed inputs.
- `PvpMatchRuntime.ApplyAuthoritativeResult(...)` now aligns local `SnapshotVersion` to host authoritative `FinalSnapshot.SnapshotVersion`.
- This keeps next-round `StartRoundFromLiveState` snapshot versions in sync between host/client and prevents snapshot-mismatch rejection on client submission.
- Bumped `ProtocolVersion/ContentVersion` to `10`.

## 2026-04-14 - Checksum Desync Fix Attempt (Client RoundResult Immediate Apply)
- Client no longer queues authoritative snapshot for next `SwitchSides`; it now applies host `RoundResult` snapshot immediately upon message receipt.
- This targets the observed `checksum ID 12` divergence window where client had already skipped local resolve but had not yet consumed queued authoritative snapshot.
- `TrackEndTurnPatch` now sends `PvpClientSubmissionMessage` only when the ending player is local on client, preventing early/empty submission noise caused by remote end-turn replay.
- Bumped `ProtocolVersion/ContentVersion` to `9` for this resolve/apply semantic update.

## 2026-04-14 - Client Read-Only Resolve Cut (B6 first landing)
- In `CombatManager.SwitchSides` prefix, client now skips local delayed-execution + `ResolveLiveRound` entirely when round is ready to resolve.
- Client path is now read-only for round resolution: it waits for host authoritative `PvpRoundResultMessage` and applies queued authoritative snapshot.
- This reduces same-room side effects on client and is the first concrete landing for B6 (read-only playback direction).
- Bumped `ProtocolVersion/ContentVersion` to `8` to prevent mixed old/new resolve semantics in multiplayer lobbies.

## 2026-04-14 - EndTurn/Submission De-dup Stability
- `PvpMatchRuntime.LockPlayer(...)` now rejects duplicate lock requests in the same round and does not re-bump planning revision.
- `TrackEndTurnPatch` now only broadcasts planning frame + client submission when lock transition is first applied, preventing duplicate lock-time sends.
- Host `RecordNetworkSubmission(...)` now ignores identical repeated submissions for the same player/round instead of re-processing them.

## 2026-04-14 - Split-Room Submission Authority v1.5 (Host Resolver Switch)
- Fixed client submission identity selection: lock-time submission now resolves to `RoomSession.LocalPlayerId`, avoiding sender/player mismatch rejections.
- `PvpNetBridge.SendClientSubmission` now auto-corrects mismatched caller id and emits explicit warnings when submission rows are missing.
- Host `ResolveLiveRound` now uses `BuildResolverSubmissions(...)`: in split-room mode, opponent submission is taken from `NetworkSubmissionsByPlayer` first, with fallback to compiled local log only when missing.
- Host now maps accepted network lock state into round phase progression (`LockedWaitingPeer/Resolving`) and updates first-finisher metadata from network submission when needed.
- Bumped `ProtocolVersion/ContentVersion` to `7` for this resolver-input behavior change.

## 2026-04-14 - Split-Room Submission Channel v1 (Client -> Host)
- Added `PvpClientSubmissionMessage` as a dedicated split-room submission channel (`roomSession + topology + round/snapshot + submission packet`).
- Client now sends its own planned submission at lock time (`EndTurn`) via `PvpNetBridge.SendClientSubmission`.
- Host now receives and validates submission context/sender/snapshot via `PvpMatchRuntime.RecordNetworkSubmission`, then stores per-player network submissions for parity diagnostics.
- Resolver path still uses existing planning compilation, but now logs `SubmissionParity` in split-room mode to compare compiled vs network-submitted signatures.
- Bumped `ProtocolVersion/ContentVersion` to `6` to prevent mixed old/new submission channel behavior.

## 2026-04-14 - Split-Room Default On + Reconnect Backlog Added
- Changed split-room toggle default: when `PTPVP_ENABLE_SPLIT_ROOM` is unset, split-room mode is now enabled by default for current testing line.
- You can still force shared-combat fallback by setting `PTPVP_ENABLE_SPLIT_ROOM=0` (or `false`).
- Added reconnect/resume backlog section to `analysis/待办清单.md` (`D1`~`D5`).

## 2026-04-14 - Split-Room Entry v1 Shell (Local Dummy View)
- In split-room mode (`PTPVP_ENABLE_SPLIT_ROOM=1`), target selection now allows the arena dummy and maps dummy-target actions to opponent side (`EnemyFrontline`/`EnemyHero`) at action-capture time.
- Added split-room combat visual layout branch:
  - local player/frontline remain visible and interactable
  - remote player/frontline visuals are hidden
  - dummy target is shown and positioned as the local room target anchor
- Goal: break same-screen face-to-face combat feel as the first entry-flow cut while keeping current authoritative round resolution pipeline unchanged.

## 2026-04-14 - Split-Room Phase-1 Scaffold (Room Session + Protocol Guard)
- Added `PvpRoomSession` scaffold with deterministic `roomSessionId` and topology (`SharedCombat` / `SplitRoom`) resolved from env toggle `PTPVP_ENABLE_SPLIT_ROOM`.
- Injected room session context into runtime/planning model (`PvpMatchRuntime.RoomSession`, `PvpRoundState`, `PvpPlanningFrame`) and added debug logs.
- Extended network payloads (`PvpRoundStateMessage`, `PvpPlanningFrameMessage`, `PvpRoundResultMessage`) with `roomSessionId + roomTopology`.
- Added receiver-side room-context guard in `PvpNetBridge`; mismatched session/topology packets are now ignored with warning logs.
- Extended lobby version compatibility lock to include split-room toggle (`R on/off`) to prevent mixed topology lobbies.
- Added project backlog document `analysis/待办清单.md`, including automated-regression and split-room milestone tasks.

## 2026-04-14 - Post-Match Turn-Start Hard Stop
- Added match-end prefix short-circuit for `CombatManager.StartTurn`: returns `Task.CompletedTask` in debug arena once match ended.
- Added match-end prefix short-circuit for `CombatManager.SetupPlayerTurn`: skips player-turn setup in the same post-match window.
- Goal: remove end-of-match extra turn-start noise (dead-player ready/unpause tick) and reduce tail async churn after game-over transition.

## 2026-04-14 - Prediction Drift Cleanup (Dead Target Block/Heal Guard)
- Delta planner and prediction engine now treat `CurrentHp <= 0` as non-receivable for `GainBlock/Heal/GainMaxHp` operations.
- Damage application now clears block when a target is reduced to `0` HP, aligning predicted dead-target post-state with live combat snapshots.
- Goal: remove noisy `prediction drift` lines where prediction still showed block on a 0-HP target while actual snapshot had block 0.

## 2026-04-13 - Match-End Guard Hardening (State Registry + Shop Bridge)
- Added `ParallelTurnMatchStateRegistry` as an out-of-band ended-state source (`ConditionalWeakTable<RunState,...>`), so post-match guards no longer depend only on modifier field lifetime.
- Match start now calls `MarkStarted(runState)` in combat setup; match end flow now calls `MarkEnded(runState)` before game-over transition.
- Rewired post-match guards (`SwitchSides` prefix/postfix, `CheckWinCondition`, `SwitchFromPlayerToEnemySide`, `StartTurn` finalizer, round tracking) to use the registry-backed ended check.
- Added ended-state guard to `PvpShopBridgeSwitchSidesPatch` so shop open/close hooks no longer run after match end.
- Goal: stop accidental round-advance/shop-open after game-over and reduce remaining post-match `StartTurn` nullable tail errors.

## 2026-04-13 - Match-End Enemy-Turn Guard
- Added guard patch on `CombatManager.SwitchFromPlayerToEnemySide`: when debug PvP match is already marked ended, skip enemy-turn switch and return completed task.
- Added defensive finalizer on `CombatManager.StartTurn` to suppress post-match nullable exception noise (`Nullable object must have a value`) in the debug arena.
- Added post-match guard for action tracking and round-switch patches so `PlayCard/UsePotion/EndTurn` logging and round progression logic no longer append new PvP actions after `MatchEnded=true`.
- Goal: remove end-of-match asynchronous enemy-turn tail errors and keep dual-log diagnostics clean.

## 2026-04-13 - Overlay: Authoritative Snapshot Panel
- Added `[权威结算快照]` section to combat intent overlay.
- Panel now shows the host-authoritative final snapshot after each resolved round:
  - snapshot version
  - `我方/对方` hero HP/MaxHP/block
  - `我方/对方` frontline exists + HP/MaxHP/block
- This is a diagnostics-only UI improvement; no protocol or combat behavior changes.

## 2026-04-13 - Delayed Afterlife Full Cut (No-Frontline Case Included)
- `PvpDelayedExecution.ShouldDelayLiveApply(Player, modelEntry)` no longer special-cases `AFTERLIFE` to immediate mode when frontline is missing.
- `AFTERLIFE` now always follows delayed apply in debug arena when delayed mode is enabled, including the "summon from empty frontline" case.
- Strengthened delayed summon entity resolution by adding `player.Osty` fallback before combat-creature scan, reducing summon command drop risk when no living frontline is currently attached.

## 2026-04-13 - RoundResult Snapshot MaxHp Sync Fix (Desync ID24)
- Root cause of latest desync: authoritative `PvpRoundResultMessage` only carried HP/Block/Exists, so client-side queued snapshot apply reconstructed stale `MaxHp` from round-start snapshot.
- Added `hero1MaxHp/hero2MaxHp/frontline1MaxHp/frontline2MaxHp` to `PvpRoundResultMessage` serialize/deserialize payload.
- `PvpNetBridge.CreateSnapshotFromMessage` now uses message-side max-hp fields instead of round-start fallback.
- `PvpNetBridge.ApplyCreatureSnapshot` now aligns hero max-hp before hp/block apply.
- Bumped `ProtocolVersion` and `ContentVersion` to `3` to hard-stop mixed old/new packet layouts in lobby.

## 2026-04-13 - Delayed Attack Cut (Strike / Poke / BreakFormation)
- Added delayed-live interception for `STRIKE_NECROBINDER` and `POKE` (`OnPlay` short-circuit in debug arena when delayed mode is enabled).
- Added delayed-live guard in custom `BREAK_FORMATION` so immediate damage is skipped and moved to round-switch apply.
- Extended delayed plan pipeline to carry cross-player damage:
  - `PvpDelayedCandidateKind.CrossDamage`
  - `PvpDelayedCommandKind.Damage`
  - delayed fingerprint now includes `Damage` commands.
- `PvpDelayedExecution` now applies delayed damage commands in the SwitchSides bridge, with deterministic block-first consumption and HP deduction logs.
- Updated overlay delayed-summary labels to include the new damage candidate/command kind.
- Bumped `ProtocolVersion` and `ContentVersion` to `2` to force lobby-side version compatibility for this behavior change.

## 2026-04-13 - Potion Tracking Fallback Hardening
- Removed an over-strict prefix guard that skipped pre-consumption capture when `PlayerChoiceContext` was null at prefix timing.
- Added `UsePotionAction.ToString()` regex fallback (`POTION.<ID>`) so tracked model id can still be resolved when slot lookup fails in prefix/postfix.
- Goal: eliminate normal-flow `UsePotion/POTION_SLOT_x` records and keep delayed planner semantics deterministic for potion actions.

## 2026-04-13 - Potion Tracking Determinism Fix (UsePotion Prefix Capture)
- `TrackPotionUsagePatch` now captures potion model id in `UsePotionAction.ExecuteAction` prefix before the slot item is consumed.
- Postfix tracking now prefers the prefix-captured model id and only falls back to post-execute slot lookup / `POTION_SLOT_x` as a last resort with explicit warning logs.
- Fixed potion target resolution guard to use `TargetId.HasValue` (so target id `0` is no longer dropped by `> 0` checks).
- Added Chinese debug logs for potion action tracking (`owner/actionId/model/slot/target`) to simplify dual-machine desync diagnosis.

## 2026-04-13 - Delayed Afterlife Bridge + Frontline Snapshot Apply
- Added `AFTERLIFE` into delayed live-apply set, with a guard: only delay when the owner frontline is currently alive at play time.
- Extended delayed command execution to support `GainMaxHp` and `SummonFrontline` in the stable internal path (no blocking native cmd wait).
- Client authoritative snapshot apply now includes frontline state (exists/maxHp/hp/block) instead of skipping all frontline updates.
- Round-result handling on client keeps queue-then-apply timing (apply in `SwitchSides` prefix) to avoid checksum drift caused by early apply before checksum capture.
## 2026-04-12 - Delayed Apply Scope Expanded to Block/Blood Potion
- Added `BLOCK_POTION` and `BLOOD_POTION` into delayed live-apply model set.
- In debug arena delayed mode, these two potions now skip immediate effect and land at round switch through delayed command plan (`GainBlock/Heal`), aligned with existing delayed-defend/self-heal pipeline.
- This intentionally does not include `AFTERLIFE` yet, because summon/max-hp delayed live commands still need a dedicated safe apply path.
## 2026-04-12 - Hotfix: Revert Native Delayed Apply Sync Wait (Freeze Fix)
- Reverted delayed apply from synchronous native CreatureCmd waiting back to stable internal apply path.
- Root cause: blocking wait in SwitchSides delayed pipeline could stall the game loop (both peers stuck after 延迟执行桥 log).
- Kept larger delayed float feedback; mode log now marks stable_internal.
## 2026-04-12 - Delayed Apply Native Feedback Attempt
- Delayed Heal/GainBlock now try native CreatureCmd execution first, so the game can render its built-in combat feedback style when available.
- Added safe fallback to internal apply + custom float when native command invocation fails.
- Increased fallback float style size for readability (ont_size=40, thicker outline).
## 2026-04-12 - Delayed Apply Floating Feedback
- Added delayed-apply floating text feedback in combat UI.
- When delayed commands land, the overlay now spawns short float labels near the target (e.g. +3 for heal, +5 格挡 for block).
- This change is visual only and does not alter delayed command resolution order.
## 2026-04-12 - Delayed Command Default Enabled For Current Test Line
- PTPVP_ENABLE_LIVE_DELAYED now defaults to ON when unset, so both machines run delayed command flow without manual env setup.
- You can still force OFF by setting PTPVP_ENABLE_LIVE_DELAYED=0.
## 2026-04-12 - Delayed Mode Early-Lock Heal Routed into Command Plan
- Added FirstFinisherPlayerId metadata to execution plans so the resolver can deterministically identify the round's first locker.
- In delayed mode (PTPVP_ENABLE_LIVE_DELAYED=1), END_TURN now emits a synthetic EARLY_LOCK_REWARD self-heal delta (+3) for the first finisher, compiled into delayed commands and applied during round switch.
- Added EARLY_LOCK_REWARD to delayed live-apply whitelist.
- Updated legacy ApplyFirstLockRewardIfPending path to mark the reward as handled by delayed command flow instead of attempting immediate live execution.
## 2026-04-12 - Round-1 Init Timing Unification (Defer SetUpCombat Init)
- Removed eager runtime round initialization from `CombatManager.SetUpCombat` patch.
- Round-1 state is now consistently initialized via first tracked action (existing lazy init path), which avoids host/client skew where SetUpCombat snapshot timing could differ on frontline summon state.
- Kept PvP message handler registration and debug target logs in SetUpCombat.
## 2026-04-12 - Round-Start Planning Snapshot Block Normalization
- `StartRoundFromLiveState` now normalizes round-start planning snapshot block values to `0` for both heroes and frontlines.
- This keeps prediction/delta planning aligned with live round-boundary semantics where block has already decayed before planned actions are resolved.
- Added round-start hero snapshot logging (`heroes=[...]`) so block/HP baseline is visible in host/client logs.
## 2026-04-12 - Prediction Alignment: Explicit Frontline Hit No Overflow in Live Shell
- Updated `PvpDeltaPlanner.ResolveAttack` for `EnemyFrontline` target:
  - if frontline exists, apply damage to frontline only (no overflow spill to hero)
  - if frontline is gone at resolution time, fallback to hero
- This aligns prediction with current instant-combat behavior and removes repeated `+5/-5` hero drift caused by synthetic overflow entries.
## 2026-04-12 - Self-Target Logging Aligned for Frontline Brace/Salve
- Round tracking now records resolved self target for `FRONTLINE_BRACE` and `FRONTLINE_SALVE` (`SelfFrontline` vs `SelfHero`) instead of always storing `None`.
- Delta planner now prefers recorded action target for these self effects, and only falls back to state probing when no explicit target was captured.
- Goal: reduce prediction drift from action-time frontline semantics diverging from planner-time inference.
## 2026-04-12 - Prediction Drift Fix: Frontline Overflow on Explicit Frontline Target
- Updated `PvpDeltaPlanner.ResolveAttack` so explicit `EnemyFrontline` attacks also honor frontline overflow semantics when frontline is alive.
- This aligns prediction with current PvP rule: frontline takes damage first, unblocked overflow spills to hero.
- Expected effect: reduce repeated `PredictionCompared` hero drift cases that were off by frontline overflow amounts.
## 2026-04-12 - Fingerprint Ignores Non-State Delayed Commands
- Updated delayed fingerprint computation to ignore `EndRoundMarker` and non-positive-amount delayed commands.
- Fingerprint now tracks only state-mutating delayed commands (`GainBlock/Heal/GainResource/GainMaxHp/SummonFrontline`) to avoid first-round false alarms from benign sequencing differences.
- No combat behavior change.
## 2026-04-12 - Delayed Fingerprint Check Uses Authoritative Planning First
- Changed delayed fingerprint verification to prefer `LastAuthoritativePlanningFrame.Submissions` for the same round before falling back to local planning logs.
- This removes first-round false-positive mismatch warnings caused by local/log ordering while still detecting real host-authoritative plan mismatch.
- Kept local-log fingerprint comparison as a secondary diagnostic (`authoritative match with local drift`) for timing/debug visibility.
## 2026-04-12 - Delayed Fingerprint Guard (No Behavior Change)
- Added `PvpDelayedPlanFingerprint` and attached delayed-command fingerprint/count to `PvpRoundResultMessage`.
- Client now recomputes local delayed command plan from current planning submissions and compares against host fingerprint.
- On mismatch, emits a dedicated warning log (`Delayed fingerprint mismatch`) before checksum divergence escalates, to make delayed-path regressions diagnosable earlier.
## 2026-04-12 - Authoritative Network Summary Includes Delayed Plan Events
- Expanded `PvpNetBridge` network summary filter to include delayed-plan events: `DelayedPlanBuilt`, `DelayedCommandPlanBuilt`, `DelayedCandidateScheduled`, `DelayedCommandScheduled`.
- Increased network summary event budget from 32 to 48 so delayed + playback + prediction diagnostics can coexist in one host round summary.
- No live combat behavior change in this update; this is for host-authoritative observability and future delayed-sync verification.
## 2026-04-12 - Hotfix: Revert Risky Delayed Live Scope
- Reverted delayed-live whitelist back to stable custom-only entries: `FRONTLINE_BRACE`, `FRONTLINE_SALVE`.
- Rolled delayed-live default back to OFF (env `PTPVP_ENABLE_LIVE_DELAYED` now opt-in again).
- Reason: extending delayed-live to vanilla `AFTERLIFE/DEFEND/BLOCK_POTION/BLOOD_POTION` caused host/client command-plan divergence and checksum `StateDivergence` at enemy-turn start.
## 2026-04-12 - Delayed Live Apply Expanded to Vanilla Defend/Afterlife/Potions
- Expanded delayed-live model set to: `DEFEND_NECROBINDER`, `AFTERLIFE`, `BLOCK_POTION`, `BLOOD_POTION`, `FRONTLINE_BRACE`, `FRONTLINE_SALVE`.
- Added Harmony interception patches for `DefendNecrobinder.OnPlay`, `Afterlife.OnPlay`, `BlockPotion.OnUse`, and `BloodPotion.OnUse` so their immediate effects are skipped and applied at round switch.
- Kept `ENERGY_POTION` immediate because delaying resource gain would break same-round planning/playability in the current live-combat shell.
## 2026-04-12 - Lobby Guard for Live Delayed Toggle
- Added `LiveDelayedApplyEnabledField` into `ParallelTurnPvpDebugModifier` and lock it into the created debug-lobby modifier.
- Extended debug-lobby mismatch checks from `P/C` to `P/C/D` (`D` = `PTPVP_ENABLE_LIVE_DELAYED` on/off), so host/client cannot ready with different delayed-execution mode.
- Added lobby configuration logs that print `liveDelayed=on/off` to speed up two-machine mismatch diagnosis.
## 2026-04-12 - Delayed Execution Uses Command Plan
- Refactored `PvpDelayedExecution.ApplyDelayedLiveEffects` to use the new bridge chain end-to-end: `ExecutionPlan -> DeltaPlan -> DelayedPlan -> DelayedCommandPlan`.
- Kept live delayed execution default-off for stability, and made it switchable via env var `PTPVP_ENABLE_LIVE_DELAYED` (`1`/`true` to enable).
- Added Chinese debug logs for delayed bridge counts and command-level apply/skip reasons, so host/client log diffing is easier in two-machine runs.
## 2026-04-12 - Overlay Clarifies Frontline Guard Rule
- The intent/summary overlay now explicitly states the current live-combat-shell rule: attacks against a frontline consume the owning hero's block first.
- FRONTLINE_BRACE and frontline damage summary lines are now rendered with that rule in plain Chinese so test results are easier to read without cross-checking backend logs.

## 2026-04-12 - Frontline Brace Hero Block
- FrontlineBrace now grants block to the hero instead of the frontline creature. This matches vanilla pet damage semantics, where attacks against a frontline consume the owning hero's block first.
- Updated DeltaPlan and card localization so the live behavior, prediction layer, and player-facing text all describe the same protection model.

## 2026-04-12 - Frontline Damage Consumes Hero Block
- Prediction now mirrors vanilla CreatureCmd.Damage for pet/frontline damage: attacks against a frontline consume the owning hero's block, not the frontline's own block value.
- This specifically tightens drift around POKE, STRIKE_NECROBINDER, and BREAK_FORMATION when they hit Osty/frontline targets while the hero still has block.

## 2026-04-12 - BoundPhylactery Summon Semantics
- DeltaPlan/Prediction now model BOUND_PHYLACTERY as OstyCmd.Summon(1) from round 2 onward: if a frontline is alive it gains +1 MaxHp/+1 Heal, otherwise a new 1/1 frontline is created before planning actions resolve.
- SummonFrontline application now mirrors the live summon command more closely by upgrading an existing living frontline instead of blindly resetting it.

# CHANGELOG

## 2026-04-11 Prediction Bridge - POKE Delta Fix
- fixed POKE being dropped from DeltaPlan when the prediction bridge redundantly re-validated frontline-attacker prerequisites against the round snapshot
- the delta/prediction layer now trusts already-executed vanilla actions instead of silently skipping legal logged attacks
- this specifically targets round-summary drift where live combat showed damage from POKE but the predicted snapshot did not

## 2026-04-11 Network Summary + Frontline Snapshot Alignment
- compacted `PvpRoundResultMessage` payloads so clients receive a short round summary instead of the full verbose resolver event stream every round
- trimmed per-event network text length and append an explicit "trimmed for network" note when the host keeps more events locally than the client summary receives
- aligned predicted frontline death snapshots with live combat by zeroing frontline `MaxHp` when the predicted frontline dies, reducing false drift noise in round summaries

## 2026-04-11 J Drive MCP Deployment
- replaced the old winget-based secondary-only MCP installer with a shared Setup-McpControl.ps1 flow that installs portable Node 22, portable Python 3.12.10, VS2022 Build Tools, and mcp-control under J:\Tools\MCPControl
- added Setup-McpControlPrimary.ps1, Setup-McpControlSecondary.ps1, and Configure-CodexMcp.ps1 so primary/secondary setup and Codex MCP registration are explicit and repeatable
- installed and verified the primary machine stack on J:, generated J:\Tools\MCPControl\Start-McpControl.ps1, and wrote primary-desktop / secondary-desktop into C:\Users\Administrator\.codex\config.toml
- synced the latest MCP scripts to \\DESKTOP-U51KJJ2\SlayTheSpire2\tools\desktop_mcp\mcpcontrol and added a handoff doc for the next session
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





## 2026-04-11 Version Guard + Intent Budget Alignment
- aligned the limited-intent reveal budget with the locked design: only played cards increase reveal budget, while potions still occupy intent slots
- updated the combat overlay copy so the rule is explicit in-game and the panel distinguishes reveal budget from total opponent slots
- added a lobby-side version guard for `ParallelTurnPvpDebugModifier`; mismatched protocol/content versions now disable ready and surface the mismatch in the custom run screen
- added `tools/local_fastmp/Invoke-ParallelTurnStartupSmokeTest.ps1` for repeatable local fastmp boot smoke testing
- wired the smoke test into `build.ps1` / `src/ParallelTurnPvp/build.ps1` via `-SmokeTestStartup` so local build + boot verification can run in one command

## 2026-04-11 Local FastMP Handoff
- added `--parallelturnpvphost` auto-host wiring plus host-ready waiting to the generated local launcher flow so the host actually creates a DirectConnectIP room before any auto-join attempt
- confirmed by adaptive local testing that the host now reaches `DirectHost started on port 33771` and opens the PvP custom-run lobby path correctly
- confirmed the next blocker is no longer room creation timing: local `--fastmp=join -clientId=1001` fails during ENet handshake because the client sends net ID `1001` but receives the host Steam ID back, producing `DisconnectionReason InternalError`
- recorded the handoff that true lobby/join validation should move back to real dual-machine `DirectConnectIP` testing, because that path uses `DirectClientConnectionInitializer netId=<SteamId>` rather than the local fastmp fake client-id flow
# 2026-04-11
- Aligned `DeltaPlan` frontline targeting semantics with explicit player target selection. Explicit `EnemyFrontline` actions no longer fall back to `EnemyHero` just because the round-start snapshot missed frontline state.
- Extended `StartRoundFromLiveState` logging to include per-player frontline snapshot state for drift diagnosis.
- Modeled `BoundPhylactery` frontline passive (`+1 MaxHp/+1 Heal`) in the delta/prediction layer so round summaries stop drifting by 1 HP each round.


## 2026-04-11 Prediction Bridge - Runtime Ordering Alignment
- changed PvpExecutionPlanner to prioritize 
untimeActionId ordering whenever live action ids are available
- this keeps prediction/summary closer to the current instant-combat shell, where effects resolve in actual execution order rather than future phase order
- phase labels are still retained for migration work, but comparison drift should now better reflect real combat timing


## 2026-04-12 Prediction Drift Trace
- added per-creature prediction trace events when a predicted snapshot diverges from actual live combat
- drift logs now include the recent delta operations that targeted the drifting hero/frontline, making remaining bridge mismatches inspectable without guesswork





## 2026-04-12 - Independent Frontline Block Pool
- Added live combat patches so frontline damage consumes frontline block instead of the owner hero block in the debug arena.
- Disabled DieForYou redirect and shared TrackBlockStatus behavior for frontline visuals in the debug arena.
- Restored FrontlineBrace to frontline block semantics and updated prediction/UI text to match the independent block pool.
## 2026-04-12 - Playback Timeline Bridge
- Added an authoritative round playback plan built from the execution plan and delta plan.
- Appended playback events to round summaries and exposed a dedicated playback section in the combat intent overlay.
- This is a bridge toward future end-of-round playback without changing the stable live combat shell.

## Authoritative Playback Summary
- Included PlaybackPlanBuilt and PlaybackEventScheduled in the host network summary so clients can render [回合回放] from authoritative round results instead of relying on local reconstruction only.
- Increased the round-result network event budget to 32 entries so playback items survive trimming in normal combat rounds.


## Delayed Candidate Bridge
- Added PvpDelayedPlanner to extract live-safe delayed-resolution candidates from DeltaPlan without changing current combat behavior.
- Resolver now emits DelayedPlanBuilt and DelayedCandidateScheduled so the overlay can show a dedicated [延迟结算候选] section before actual delayed application is implemented.


## Delayed Command Bridge
- Added PvpDelayedCommandPlanner to compile delayed candidates into concrete execution-script entries with executor hints such as CreatureCmd.GainBlock(frontline) and OstyCmd.Summon(amount).
- Resolver now emits DelayedCommandPlanBuilt and DelayedCommandScheduled, and the overlay renders them under [延迟落地脚本] for implementation review before live cutover.


































## 2026-04-28 - BoundPhylactery Alive Frontline Prediction Fix
- Latest dual-machine logs confirmed client attacks now enter host authoritative resolution.
- Found remaining numeric drift: living Osty gained +1 MaxHp/+1 HP from BoundPhylactery live behavior, but DeltaPlan only modeled the missing-frontline 1/1 summon branch.
- Updated DeltaPlan passive modeling so BoundPhylactery mirrors OstyCmd.Summon(1): living frontline gets +1 MaxHp and +1 Heal; missing frontline gets fresh 1/1 summon.
- Note: when a frontline is missing, the current PvP rule still allows BoundPhylactery to create a 1/1 guard before planned attacks resolve, so the first face attack can lose 1 damage to that guard by design.

## 2026-05-06 - Locked Action Noise Cleanup
- Reordered `AppendAction` duplicate detection ahead of the locked-log guard so repeated late hook hits after `EndTurn` are treated as duplicates instead of warning spam.
- Preserved the real locked-log warning for truly new illegal actions after a player has already locked.
- When a new action is rejected because the log is locked, the temporary dedupe reservation is removed so a later authoritative replay can still append the same action if needed.
- Added a per-round duplicate-action log budget so normal repeated hook hits still leave a few samples in the log, then collapse into a single suppression notice instead of flooding the file.

## 2026-05-07 - Shared Combat Read-Only Resolve Checksum Bypass
- Fixed a `StateDivergence` regression in shared-combat debug PvP when the client queued and applied an authoritative round-result snapshot after host-authoritative read-only resolve.
- Expanded the vanilla checksum bypass guard from split-room only to all debug-arena runs that use host-authoritative client read-only resolve.
- This keeps vanilla checksum from disconnecting the session on intentional host-authoritative runtime corrections, while split-room behavior remains covered by the same bypass path.

## 2026-05-07 - Match-End Runtime Guard Tightening
- When the debug arena match ends, the PvP runtime now explicitly enters `MatchEnd`, marks the round resolved, and clears any pending authoritative snapshot.
- `ShouldStartRound` now rejects any follow-up live round start once runtime is in `MatchEnd`, reducing tail-end player-turn startup noise after the game-over flow begins.

## 2026-05-07 - Resume Flow Shared-Combat Parity
- Expanded `ResumeState` polling and live-snapshot restore from split-room-only gating to all debug-arena runs that use host-authoritative snapshot sync.
- On client resume, authoritative `RoundState` now reapplies the round-start snapshot, and authoritative `RoundResult` can directly restore the final snapshot instead of waiting for a future `SwitchSides` hook.
- Updated disconnect UI copy to reflect the current behavior: the session is frozen first, then the system attempts recovery before asking the player to restart.
