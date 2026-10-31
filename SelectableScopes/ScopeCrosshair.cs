using GameData;
using Gear;
using Player;

namespace SelectableScopes;

internal static class ScopeCrosshair
{
    private const string SightName = "Sight_11";

    private static ItemDataBlock Forced;

    private static bool OriginalValue;

    private static bool Holding;

    private static bool Logged;

    internal static void Prepare(FirstPersonItemHolder holder)
    {
        if (!Enabled())
        {
            Release();
            return;
        }

        if (!IsLocal(holder))
        {
            return;
        }

        ItemEquippable weapon = holder.WieldedItem;
        if (weapon == null || !IsSight11(weapon))
        {
            Release();
            return;
        }

        ItemDataBlock block = weapon.ItemDataBlock;
        if (block == null)
        {
            Release();
            return;
        }

        if (!Holding || Forced == null || Forced.persistentID != block.persistentID)
        {
            Release();
            OriginalValue = block.ShowCrosshairWhenAiming;
            Forced = block;
            Holding = true;
        }

        if (!block.ShowCrosshairWhenAiming)
        {
            block.ShowCrosshairWhenAiming = true;
        }
    }

    internal static void Keep(FirstPersonItemHolder holder)
    {
        if (!Enabled() || holder == null || holder.m_currentState != FPItemStateName.Aim || !IsLocal(holder))
        {
            return;
        }

        ItemEquippable weapon = holder.WieldedItem;
        if (weapon == null || !IsSight11(weapon))
        {
            return;
        }

        LerpingPairFloat alpha = holder.CrosshairAlpha;
        if (alpha != null && (alpha.Target < 0.999f || alpha.Start < 0.999f))
        {
            alpha.Start = 1f;
            alpha.Target = 1f;
        }

        Show(weapon);
    }

    private static void Show(ItemEquippable weapon)
    {
        if (!InFirstPerson() || GuiManager.Current == null)
        {
            return;
        }

        CrosshairGuiLayer layer = GuiManager.CrosshairLayer;
        ItemDataBlock block = weapon.ItemDataBlock;
        if (layer == null || block == null)
        {
            return;
        }

        float shown;
        try
        {
            shown = CellSettingsApply.ApplyHUDCrosshairOpacity(1f);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Crosshair opacity: " + ex.Message);
            shown = 1f;
        }

        if (shown <= 0.001f)
        {
            return;
        }

        Crosshair wanted = block.crosshair;
        if (wanted == Crosshair.None)
        {
            wanted = Crosshair.Neutral;
        }

        if (layer.m_currentCrosshair != wanted)
        {
            layer.ShowCrosshair(wanted);
        }

        if (layer.GetCrosshairAlpha() < shown * 0.9f)
        {
            layer.SetCrosshairAlpha(shown);
        }

        if (!Logged)
        {
            Logged = true;
            Plugin.LogSource.LogInfo("Sight 11 keeps the crosshair while aiming.");
        }
    }

    private static bool InFirstPerson()
    {
        if (FocusStateManager.Current == null)
        {
            return false;
        }

        eFocusState state = FocusStateManager.CurrentState;
        return state == eFocusState.FPS || state == eFocusState.FPSNSpace;
    }

    private static void Release()
    {
        if (!Holding)
        {
            return;
        }

        Holding = false;
        ItemDataBlock block = Forced;
        Forced = null;
        if (block != null)
        {
            block.ShowCrosshairWhenAiming = OriginalValue;
        }
    }

    private static bool IsSight11(ItemEquippable weapon)
    {
        GearPartHolder parts = weapon.GearPartHolder;
        GearSightPartDataBlock sight = parts != null ? parts.SightData : null;
        return sight != null && string.Equals(sight.name, SightName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocal(FirstPersonItemHolder holder)
    {
        if (holder == null)
        {
            return false;
        }

        PlayerAgent owner = holder.m_owner;
        if (owner != null)
        {
            return owner.IsLocallyOwned;
        }

        PlayerAgent local;
        return PlayerManager.TryGetLocalPlayerAgent(out local) && local != null && local.FPItemHolder == holder;
    }

    private static bool Enabled()
    {
        return Plugin.Enabled != null && Plugin.Enabled.Value;
    }
}
