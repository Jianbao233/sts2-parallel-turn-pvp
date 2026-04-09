using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using ParallelTurnPvp.Bootstrap;

namespace ParallelTurnPvp.Patches;

[HarmonyPatch(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu._Ready))]
public static class AddParallelTurnMainMultiplayerButtonPatch
{
    private const string ButtonName = "ParallelTurnPvpButton";

    static void Postfix(NMultiplayerSubmenu __instance)
    {
        InjectButton(__instance);
        SyncButtonVisibility(__instance);
    }

    internal static void InjectButton(NMultiplayerSubmenu submenu)
    {
        if (submenu.HasNode($"ButtonContainer/{ButtonName}"))
        {
            return;
        }

        if (!submenu.HasNode("ButtonContainer/HostButton"))
        {
            return;
        }

        NSubmenuButton hostButton = submenu.GetNode<NSubmenuButton>("ButtonContainer/HostButton");
        if (hostButton.GetParent() is not Control parent)
        {
            return;
        }

        if (hostButton.Duplicate() is not NSubmenuButton pvpButton)
        {
            return;
        }

        pvpButton.Name = ButtonName;
        parent.AddChild(pvpButton);
        parent.MoveChild(pvpButton, hostButton.GetIndex() + 1);
        pvpButton.SetIconAndLocalization("PARALLELTURN_PVP");
        pvpButton.SelfModulate = new Color(1.00f, 0.91f, 0.72f, 1.00f);
        pvpButton.GetNode<CanvasItem>("BgPanel").SelfModulate = new Color(0.95f, 0.80f, 0.42f, 1.00f);
        pvpButton.GetNode<CanvasItem>("Icon").SelfModulate = new Color(0.96f, 0.88f, 0.55f, 1.00f);

        foreach (Godot.Collections.Dictionary connection in pvpButton.GetSignalConnectionList(NClickableControl.SignalName.Released))
        {
            var callable = connection["callable"].AsCallable();
            if (pvpButton.IsConnected(NClickableControl.SignalName.Released, callable)) { pvpButton.Disconnect(NClickableControl.SignalName.Released, callable); }
        }

        pvpButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
        {
            Log.Info("[ParallelTurnPvp] Multiplayer main menu button pressed.");
            Control loadingOverlay = submenu.GetNode<Control>("%LoadingOverlay");
            NSubmenuStack stack = Traverse.Create(submenu).Field("_stack").GetValue<NSubmenuStack>();
            TaskHelper.RunSafely(ParallelTurnPvpArenaBootstrap.StartHostDebugAsync(loadingOverlay, stack));
        }));
    }

    internal static void SyncButtonVisibility(NMultiplayerSubmenu submenu)
    {
        if (!submenu.HasNode($"ButtonContainer/{ButtonName}") || !submenu.HasNode("ButtonContainer/HostButton"))
        {
            return;
        }

        NSubmenuButton hostButton = submenu.GetNode<NSubmenuButton>("ButtonContainer/HostButton");
        NSubmenuButton pvpButton = submenu.GetNode<NSubmenuButton>($"ButtonContainer/{ButtonName}");
        pvpButton.Visible = hostButton.Visible;
        pvpButton.SetEnabled(hostButton.IsEnabled);
    }
}

[HarmonyPatch(typeof(NMultiplayerSubmenu), "UpdateButtons")]
public static class SyncParallelTurnMainMultiplayerButtonPatch
{
    static void Postfix(NMultiplayerSubmenu __instance)
    {
        AddParallelTurnMainMultiplayerButtonPatch.InjectButton(__instance);
        AddParallelTurnMainMultiplayerButtonPatch.SyncButtonVisibility(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.InitializeMultiplayerAsClient))]
public static class ConfigureParallelTurnClientPatch
{
    static void Postfix(NCustomRunScreen __instance)
    {
        ParallelTurnPvpArenaBootstrap.ConfigureCustomLobbyScreen(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.InitializeMultiplayerAsHost))]
public static class ConfigureParallelTurnHostPatch
{
    static void Postfix(NCustomRunScreen __instance)
    {
        ParallelTurnPvpArenaBootstrap.ConfigureCustomLobbyScreen(
            __instance,
            ParallelTurnPvpArenaBootstrap.ConsumePendingHostDebugStart());
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.OnSubmenuOpened))]
public static class RefreshParallelTurnScreenPatch
{
    static void Postfix(NCustomRunScreen __instance)
    {
        if (ParallelTurnPvpArenaBootstrap.IsDebugScreen(__instance))
        {
            ParallelTurnPvpArenaBootstrap.ConfigureCustomLobbyScreen(__instance);
        }
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), "OnModifiersListChanged")]
public static class LockParallelTurnModifierPatch
{
    static bool Prefix(NCustomRunScreen __instance)
    {
        if (!ParallelTurnPvpArenaBootstrap.IsDebugScreen(__instance))
        {
            return true;
        }

        StartRunLobby? lobby = Traverse.Create(__instance).Field("_lobby").GetValue<StartRunLobby>();
        if (lobby != null && lobby.NetService.Type != NetGameType.Client)
        {
            lobby.SetModifiers(ParallelTurnPvpArenaBootstrap.CreateLockedModifierList().ToList());
        }

        return false;
    }
}

[HarmonyPatch(typeof(StartRunLobby), nameof(StartRunLobby.SetReady))]
public static class ForceNecrobinderOnReadyPatch
{
    static void Prefix(StartRunLobby __instance)
    {
        if (ParallelTurnPvpArenaBootstrap.IsDebugLobby(__instance))
        {
            ParallelTurnPvpArenaBootstrap.ForceLocalCharacter(__instance);
        }
    }
}
