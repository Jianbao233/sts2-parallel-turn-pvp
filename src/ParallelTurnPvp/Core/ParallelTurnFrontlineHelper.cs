using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace ParallelTurnPvp.Core;

public static class ParallelTurnFrontlineHelper
{
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
        List<Creature> targets = [];
        foreach (Player opponent in combatState.Players.Where(player => player != viewer))
        {
            Creature? frontline = GetFrontline(opponent);
            if (frontline != null && frontline.IsAlive)
            {
                targets.Add(frontline);
                continue;
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
        if (!candidate.IsAlive || IsDebugArenaDummy(candidate))
        {
            return false;
        }

        Player? owner = GetOwner(candidate);
        if (owner == null || owner == viewer)
        {
            return false;
        }

        Creature? frontline = GetFrontline(owner);
        if (candidate.IsPlayer)
        {
            return frontline == null || !frontline.IsAlive;
        }

        return candidate.PetOwner == owner && frontline == candidate;
    }

    public static PvpTargetRef CreateTargetRef(Player actor, Creature? target)
    {
        if (target == null)
        {
            return new PvpTargetRef { OwnerPlayerId = actor.NetId, Kind = PvpTargetKind.None };
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
