using HarmonyLib;
using Player;

namespace SelectableScopes.Patches;

[HarmonyPatch(typeof(FirstPersonItemHolder), "UpdateGUI")]
internal static class ScopeCrosshairPreparePatch
{
    private static bool Logged;

    private static void Prefix(FirstPersonItemHolder __instance)
    {
        try
        {
            ScopeCrosshair.Prepare(__instance);
        }
        catch (Exception ex)
        {
            if (!Logged)
            {
                Logged = true;
                Plugin.LogSource.LogWarning("Could not keep the Sight 11 crosshair: " + ex.Message);
            }
        }
    }
}

[HarmonyPatch(typeof(FirstPersonItemHolder), "UpdateGUI")]
internal static class ScopeCrosshairKeepPatch
{
    private static bool Logged;

    private static void Postfix(FirstPersonItemHolder __instance)
    {
        try
        {
            ScopeCrosshair.Keep(__instance);
        }
        catch (Exception ex)
        {
            if (!Logged)
            {
                Logged = true;
                Plugin.LogSource.LogWarning("Could not keep the Sight 11 crosshair: " + ex.Message);
            }
        }
    }
}

[HarmonyPatch(typeof(CrosshairGuiLayer), nameof(CrosshairGuiLayer.AfterCameraUpdate))]
internal static class ScopeCrosshairLayerPatch
{
    private static bool Logged;

    private static void Postfix()
    {
        try
        {
            PlayerAgent player;
            if (!PlayerManager.TryGetLocalPlayerAgent(out player) || player == null)
            {
                return;
            }

            ScopeCrosshair.Keep(player.FPItemHolder);
        }
        catch (Exception ex)
        {
            if (!Logged)
            {
                Logged = true;
                Plugin.LogSource.LogWarning("Could not keep the Sight 11 crosshair: " + ex.Message);
            }
        }
    }
}
