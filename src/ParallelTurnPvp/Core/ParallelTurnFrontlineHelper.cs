using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Models;

namespace ParallelTurnPvp.Core;

public static class ParallelTurnFrontlineHelper
{
    public static bool IsSplitRoomActive(IRunState? runState)
    {
        if (runState == null)
        {
            return false;
        }

        ParallelTurnPvpDebugModifier? modifier = runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().FirstOrDefault();
        return modifier is { SplitRoomEnabledField: true };
    }

    public static Creature? GetFrontline(Player player)
    {
        if (player.IsOstyAlive)
        {
            return player.Osty;
        }

        return player.PlayerCombatState?.Pets?.FirstOrDefault(creature => creature.PetOwner == player && creature.IsAlive);
    }

    public static Creature ResolveProtectedTarget(Creature originalTarget)
    {
        if (!originalTarget.IsPlayer || originalTarget.Player == null)
        {
            return originalTarget;
        }

        Creature? frontline = GetFrontline(originalTarget.Player);
        if (frontline != null && frontline.IsAlive)
        {
            Log.Info($"[ParallelTurnPvp] Redirected protected target from hero {originalTarget} to frontline {frontline}.");
            return frontline;
        }

        return originalTarget;
    }

    public static Creature ResolveEnemyFrontlineOrHero(Player attacker, Creature requestedTarget)
    {
        if (requestedTarget.IsPlayer && requestedTarget.Player != attacker)
        {
            return ResolveProtectedTarget(requestedTarget);
        }

        return requestedTarget;
    }

    public static Player? GetOwner(Creature creature)
    {
        return creature.Player ?? creature.PetOwner;
    }

    public static bool IsDebugArenaDummy(Creature? creature)
    {
        return creature?.Monster is BattleFriendV1 or BattleFriendV2 or BattleFriendV3;
    }

    public static IReadOnlyList<Creature> GetSelectableEnemyTargets(CombatState combatState, Player viewer)
    {
        if (IsSplitRoomActive(viewer.RunState))
        {
            Creature? dummy = combatState.Creatures.FirstOrDefault(candidate => IsDebugArenaDummy(candidate) && candidate.IsAlive);
            if (dummy != null)
            {
                return [dummy];
            }
        }

        List<Creature> targets = [];
        foreach (Player opponent in combatState.Players.Where(player => player != viewer))
        {
            Creature? frontline = GetFrontline(opponent);
            if (frontline != null && frontline.IsAlive)
            {
                targets.Add(frontline);
            }

            if (opponent.Creature.IsAlive)
            {
                targets.Add(opponent.Creature);
            }
        }

        return targets;
    }

    public static bool IsSelectableEnemyTarget(Player viewer, Creature candidate)
    {
        if (!candidate.IsAlive)
        {
            return false;
        }

        if (IsDebugArenaDummy(candidate))
        {
            return IsSplitRoomActive(viewer.RunState);
        }

        Player? owner = GetOwner(candidate);
        if (owner == null || owner == viewer)
        {
            return false;
        }

        Creature? frontline = GetFrontline(owner);
        if (candidate.IsPlayer)
        {
            // Shared-combat mainline allows explicitly choosing enemy hero even when frontline exists.
            // Whether hero damage is intercepted is decided by the resolver, not by UI target gating.
            return true;
        }

        return candidate.PetOwner == owner && frontline == candidate;
    }

    public static PvpTargetRef CreateTargetRef(Player actor, Creature? target)
    {
        if (target == null)
        {
            return new PvpTargetRef { OwnerPlayerId = actor.NetId, Kind = PvpTargetKind.None };
        }

        if (IsDebugArenaDummy(target) && IsSplitRoomActive(actor.RunState))
        {
            Player? opponent = actor.RunState.Players.FirstOrDefault(player => player.NetId != actor.NetId);
            if (opponent == null)
            {
                return new PvpTargetRef { OwnerPlayerId = actor.NetId, Kind = PvpTargetKind.None };
            }

            Creature? opponentFrontline = GetFrontline(opponent);
            return new PvpTargetRef
            {
                OwnerPlayerId = opponent.NetId,
                Kind = opponentFrontline != null && opponentFrontline.IsAlive
                    ? PvpTargetKind.EnemyFrontline
                    : PvpTargetKind.EnemyHero
            };
        }

        if (target.IsPlayer && target.Player != null)
        {
            return new PvpTargetRef
            {
                OwnerPlayerId = target.Player.NetId,
                Kind = target.Player == actor ? PvpTargetKind.SelfHero : PvpTargetKind.EnemyHero
            };
        }

        if (target.PetOwner != null)
        {
            return new PvpTargetRef
            {
                OwnerPlayerId = target.PetOwner.NetId,
                Kind = target.PetOwner == actor ? PvpTargetKind.SelfFrontline : PvpTargetKind.EnemyFrontline
            };
        }

        return new PvpTargetRef { OwnerPlayerId = actor.NetId, Kind = PvpTargetKind.None };
    }
}
