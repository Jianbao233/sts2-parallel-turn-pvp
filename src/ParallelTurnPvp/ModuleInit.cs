using System;
using System.Runtime.CompilerServices;
using Godot;

namespace ParallelTurnPvp;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        try
        {
            ParallelTurnPvpMod.Initialize();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ParallelTurnPvp] ModuleInitializer failed: {ex}");
        }
    }
}
