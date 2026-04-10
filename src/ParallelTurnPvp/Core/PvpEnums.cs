namespace ParallelTurnPvp.Core;

public enum PvpMatchPhase
{
    None,
    MatchInit,
    RoundStart,
    Planning,
    LockedWaitingPeer,
    Resolving,
    RoundEnd,
    MatchEnd
}

public enum PvpActionType
{
    PlayCard,
    UsePotion,
    EndRound,
    UndoEndRound,
    System
}

public enum PvpTargetKind
{
    None,
    SelfHero,
    SelfFrontline,
    EnemyHero,
    EnemyFrontline
}

public enum PvpIntentCategory
{
    Unknown,
    Attack,
    Guard,
    Buff,
    Debuff,
    Summon,
    Recover,
    Resource
}

public enum PvpIntentTargetSide
{
    None,
    Self,
    Enemy
}

public enum PvpResolvedEventKind
{
    RoundStarted,
    ExecutionPlanBuilt,
    PhaseStarted,
    ActionScheduled,
    ActionLogged,
    PlayerLocked,
    PlayerUnlocked,
    HeroStateChanged,
    FrontlineStateChanged,
    RoundResolved,
    MatchEnded
}

public enum PvpResolutionPhase
{
    Summon,
    Buff,
    Debuff,
    Recover,
    Resource,
    Attack,
    EndRound
}
