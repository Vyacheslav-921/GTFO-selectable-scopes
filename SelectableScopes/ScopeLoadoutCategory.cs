using CellMenu;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Player;
using SNetwork;
using TMPro;
using UnityEngine;

namespace SelectableScopes;

internal static class ScopeLoadoutCategory
{
    private const string ButtonName = "SelectableScopes.Button";

    private const string CardName = "SelectableScopes.Sight";

    private const string ScopeUpId = "\u0001scope-up";

    private const string ScopeDownId = "\u0001scope-down";

    private const string ForwardId = "\u0001scope-forward";

    private const string BackId = "\u0001scope-back";

    private const string SaveId = "\u0001align-save";

    private const string ResetId = "\u0001align-reset";

    private static readonly Color SaveColor = new Color(0.62f, 1f, 0.62f, 1f);

    private static readonly Color ResetColor = new Color(1f, 0.78f, 0.45f, 1f);

    private static readonly Color Accent = new Color(0.54f, 0.71f, 0.98f, 1f);

    private static readonly Dictionary<IntPtr, CM_LobbyScrollItem> Buttons = new Dictionary<IntPtr, CM_LobbyScrollItem>();

    private static readonly Dictionary<IntPtr, CM_PlayerLobbyBar> ButtonBars = new Dictionary<IntPtr, CM_PlayerLobbyBar>();

    private static int ReleasedFrame = -1;

    private static readonly List<GameObject> Cards = new List<GameObject>();

    private static readonly List<string> RowNames = new List<string>();

    private static readonly Color SelectedColor = new Color(0.62f, 1f, 0.62f, 1f);

    private static CM_PlayerLobbyBar OpenBar;

    private static InventorySlot OpenSlot;

    private static eFocusState SeenFocus = eFocusState.None;

    private static bool AlignOnShow;

    private static int AlignFrames;

    internal static void EnsureButton(CM_PlayerLobbyBar bar)
    {
        if (bar == null || bar.m_clothesButton == null || bar.m_weaponsHeaderText == null)
        {
            return;
        }

        try
        {
            CM_LobbyScrollItem button = ButtonFor(bar);
            if (button == null)
            {
                button = CreateButton(bar);
            }

            if (button == null)
            {
                return;
            }

            bool local = IsLocal(bar);
            button.gameObject.SetActive(local);
            if (local)
            {
                RemoveLocalizers(button.gameObject);
                LabelButton(button);
                PlaceButton(button, bar.m_weaponsHeaderText);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning("Could not add the scopes button: " + ex.Message);
        }
    }

    internal static void HideButton(CM_PlayerLobbyBar bar)
    {
        CM_LobbyScrollItem button = ButtonFor(bar);
        if (button != null)
        {
            button.gameObject.SetActive(false);
        }
    }

    internal static void ShowButton(CM_PlayerLobbyBar bar)
    {
        if (!IsLocal(bar))
        {
            HideButton(bar);
            return;
        }

        EnsureButton(bar);
    }

    private static CM_LobbyScrollItem CreateButton(CM_PlayerLobbyBar bar)
    {
        CM_LobbyScrollItem button = UnityEngine.Object.Instantiate(bar.m_clothesButton, bar.m_clothesButton.transform.parent, true);
        if (button == null)
        {
            return null;
        }

        button.gameObject.name = ButtonName;
        RectTransformComp layout = button.GetComponent<RectTransformComp>();
        if (layout != null)
        {
            layout.Setup();
            layout.enabled = false;
        }

        button.SetOnBtnPressCallback(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(_ => Open(bar))));
        Buttons[bar.Pointer] = button;
        ButtonBars[bar.Pointer] = bar;
        return button;
    }

    private static void PlaceButton(CM_LobbyScrollItem button, TextMeshPro header)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        RectTransform headerRect = header != null ? header.GetComponent<RectTransform>() : null;
        if (rect == null || headerRect == null)
        {
            return;
        }

        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        header.ForceMeshUpdate();

        Bounds textBounds = header.textBounds;
        bool hasText = textBounds.size.x > 1f && textBounds.size.y > 1f;
        float textWidth = hasText ? textBounds.size.x : Mathf.Abs(headerRect.rect.width);
        float textHeight = hasText ? textBounds.size.y : Mathf.Abs(headerRect.rect.height);
        Vector3 textRight = headerRect.TransformPoint(hasText
            ? new Vector3(textBounds.max.x, textBounds.center.y, 0f)
            : new Vector3(headerRect.rect.xMax, headerRect.rect.center.y, 0f));

        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(rect.lossyScale.x));
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(rect.lossyScale.y));
        float headerWorldHeight = Mathf.Abs(textHeight * headerRect.lossyScale.y);
        TextMeshPro label = button.m_nameText;
        float labelScaleX = label != null ? Mathf.Abs(label.transform.lossyScale.x) : 0f;
        float labelWorldWidth = label != null && label.preferredWidth > 1f && labelScaleX > 0.0001f
            ? label.preferredWidth * labelScaleX
            : Mathf.Abs(textWidth * headerRect.lossyScale.x);
        float width = (labelWorldWidth / scaleX) + 24f;
        float height = headerWorldHeight / scaleY;
        if (width < 8f)
        {
            width = Mathf.Max(8f, rect.sizeDelta.x * 0.4f);
        }

        if (height < 8f)
        {
            height = Mathf.Max(8f, rect.sizeDelta.y);
        }

        FitButton(button, width, height);

        float gap = Mathf.Max(headerWorldHeight * 0.45f, 0.01f);
        Vector3 position = rect.position;
        position.x = textRight.x + gap;
        position.y = textRight.y;
        rect.position = position;
        ClearRow(rect, textRight.x, gap);
    }

    private static void ClearRow(RectTransform rect, float titleRight, float gap)
    {
        if (rect.parent == null)
        {
            return;
        }

        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (!TryBlocker(rect, out float blockerLeft, out float blockerRight))
            {
                return;
            }

            if (!WorldSpan(rect, out float left, out float right, out _, out _))
            {
                return;
            }

            float besideTitle = titleRight + gap;
            float leftSpot = blockerLeft - gap - (right - left);
            Vector3 position = rect.position;
            position.x = leftSpot >= besideTitle ? leftSpot : blockerRight + gap;
            rect.position = position;
        }
    }

    private static bool TryBlocker(RectTransform rect, out float blockerLeft, out float blockerRight)
    {
        blockerLeft = 0f;
        blockerRight = 0f;
        Transform parent = rect.parent;
        if (parent == null || !WorldSpan(rect, out float ourLeft, out float ourRight, out float ourBottom, out float ourTop))
        {
            return false;
        }

        float ourWidth = ourRight - ourLeft;
        float ourHeight = ourTop - ourBottom;
        bool found = false;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null || child.gameObject == rect.gameObject || !child.gameObject.activeInHierarchy)
            {
                continue;
            }

            CM_LobbyScrollItem item = child.GetComponent<CM_LobbyScrollItem>();
            RectTransform other = item != null ? item.GetComponent<RectTransform>() : null;
            if (other == null || !WorldSpan(other, out float left, out float right, out float bottom, out float top))
            {
                continue;
            }

            if (right - left > ourWidth * 2.5f || top - bottom > ourHeight * 2.5f)
            {
                continue;
            }

            if (!Overlaps(ourLeft, ourRight, ourBottom, ourTop, left, right, bottom, top))
            {
                continue;
            }

            if (!found || left < blockerLeft)
            {
                blockerLeft = left;
            }

            if (!found || right > blockerRight)
            {
                blockerRight = right;
            }

            found = true;
        }

        return found;
    }

    private static bool WorldSpan(RectTransform rect, out float left, out float right, out float bottom, out float top)
    {
        left = 0f;
        right = 0f;
        bottom = 0f;
        top = 0f;
        if (rect == null)
        {
            return false;
        }

        Il2CppStructArray<Vector3> corners = new Il2CppStructArray<Vector3>(4);
        rect.GetWorldCorners(corners);
        left = Mathf.Min(Mathf.Min(corners[0].x, corners[1].x), Mathf.Min(corners[2].x, corners[3].x));
        right = Mathf.Max(Mathf.Max(corners[0].x, corners[1].x), Mathf.Max(corners[2].x, corners[3].x));
        bottom = Mathf.Min(Mathf.Min(corners[0].y, corners[1].y), Mathf.Min(corners[2].y, corners[3].y));
        top = Mathf.Max(Mathf.Max(corners[0].y, corners[1].y), Mathf.Max(corners[2].y, corners[3].y));
        return right - left > 0.0001f && top - bottom > 0.0001f;
    }

    private static bool Overlaps(float aLeft, float aRight, float aBottom, float aTop, float bLeft, float bRight, float bBottom, float bTop)
    {
        const float slop = 0.001f;
        return aLeft < bRight - slop && aRight > bLeft + slop && aBottom < bTop - slop && aTop > bBottom + slop;
    }

    private static void Open(CM_PlayerLobbyBar bar)
    {
        if (bar == null || !IsLocal(bar))
        {
            return;
        }

        if (!Plugin.Ready || ScopeCatalog.SightNames == null || ScopeCatalog.SightNames.Length == 0 || bar.m_clothesCardPrefab == null)
        {
            Plugin.LogSource.LogWarning("Scope list is not ready yet.");
            return;
        }

        try
        {
            OpenCore(bar);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning("Could not open the scope list: " + ex.Message);
        }
    }

    private static void OpenCore(CM_PlayerLobbyBar bar)
    {
        CM_ScrollWindow window = EnsureWindow(bar);
        if (window == null)
        {
            return;
        }

        OpenBar = bar;
        EnterPopupFocus();
        PlacePopup(bar);
        window.m_infoBoxWidth = 600f;
        window.gameObject.SetActive(true);
        window.SetVisible(true);
        window.ForcePopupLayer(true);
        window.SetSize(new Vector2(1600f, 760f));
        window.ResetHeaders();
        AddHeader(window, InventorySlot.GearStandard, 0, "PRIMARY");
        AddHeader(window, InventorySlot.GearSpecial, 1, "SPECIAL");
        window.SetPosition(new Vector2(0f, 350f));
        if (bar.m_popupInfoBoxWeaponPrefab != null)
        {
            window.RespawnInfoBoxFromPrefab(bar.m_popupInfoBoxWeaponPrefab);
        }

        InventorySlot slot = InventorySlot.GearStandard;
        int key = 0;
        if (GameStateManager.CurrentStateName == eGameStateName.InLevel && TryWieldedWeapon(out InventorySlot wielded))
        {
            slot = wielded;
            key = wielded == InventorySlot.GearSpecial ? 1 : 0;
        }

        Draw(slot, key, null);
    }

    private static bool TryWieldedWeapon(out InventorySlot slot)
    {
        slot = InventorySlot.GearStandard;
        try
        {
            PlayerAgent agent = PlayerManager.GetLocalPlayerAgent();
            if (agent == null || agent.Inventory == null)
            {
                return false;
            }

            InventorySlot wielded = agent.Inventory.WieldedSlot;
            if (wielded != InventorySlot.GearStandard && wielded != InventorySlot.GearSpecial)
            {
                return false;
            }

            slot = wielded;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Wielded weapon: " + ex.Message);
            return false;
        }
    }

    internal static void NoteMenuFocus()
    {
        FocusStateManager focus = FocusStateManager.Current;
        if (focus == null)
        {
            return;
        }

        eFocusState state = FocusStateManager.CurrentState;
        if (state == eFocusState.MainMenu && SeenFocus != eFocusState.MainMenu && GameStateManager.CurrentStateName == eGameStateName.InLevel)
        {
            AlignOnShow = true;
            AlignFrames = 8;
        }

        SeenFocus = state;
    }

    internal static void AlignOpenSlot()
    {
        NoteMenuFocus();
        if (!AlignOnShow || SeenFocus != eFocusState.MainMenu || GameStateManager.CurrentStateName != eGameStateName.InLevel)
        {
            return;
        }

        CM_PlayerLobbyBar bar = OpenBar;
        CM_ScrollWindow window = bar != null ? bar.m_popupScrollWindow : null;
        if (bar == null || window == null || !bar.m_popupVisible || !window.gameObject.activeInHierarchy)
        {
            AlignFrames--;
            if (AlignFrames <= 0)
            {
                AlignOnShow = false;
            }

            return;
        }

        AlignOnShow = false;
        if (!TryWieldedWeapon(out InventorySlot wielded) || wielded == OpenSlot)
        {
            return;
        }

        int key = wielded == InventorySlot.GearSpecial ? 1 : 0;
        try
        {
            Draw(wielded, key, null);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Scope menu slot: " + ex.Message);
        }
    }

    private static void AddHeader(CM_ScrollWindow window, InventorySlot slot, int key, string title)
    {
        InventorySlot capturedSlot = slot;
        int capturedKey = key;
        window.AddHeader(
            title,
            key,
            DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(_ => Draw(capturedSlot, capturedKey, null))));
    }

    private static void Draw(InventorySlot slot, int key, float? scroll)
    {
        CM_PlayerLobbyBar bar = OpenBar;
        if (bar == null || bar.m_popupScrollWindow == null || bar.m_clothesCardPrefab == null || ScopeCatalog.SightNames == null)
        {
            return;
        }

        CM_ScrollWindow window = bar.m_popupScrollWindow;
        OpenSlot = slot;
        DestroyCards();
        string current = ScopeChoices.Display(slot);
        Il2CppSystem.Collections.Generic.List<iScrollWindowContent> contents = new Il2CppSystem.Collections.Generic.List<iScrollWindowContent>();
        AddContent(contents, bar, ScopeUpId, false);
        AddContent(contents, bar, ScopeDownId, false);
        AddContent(contents, bar, ForwardId, false);
        AddContent(contents, bar, BackId, false);
        AddContent(contents, bar, SaveId, false);
        AddContent(contents, bar, ResetId, false);
        string[] sights = OrderedSights();
        for (int i = 0; i < sights.Length; i++)
        {
            string sightName = sights[i];
            AddContent(contents, bar, sightName, string.Equals(sightName, current, StringComparison.Ordinal));
        }

        window.SetContentItems(contents, 0f);
        for (int i = 0; i < Cards.Count; i++)
        {
            GameObject cardObject = Cards[i];
            if (cardObject == null || i >= RowNames.Count)
            {
                continue;
            }

            CM_LobbyScrollItem card = cardObject.GetComponent<CM_LobbyScrollItem>();
            if (card == null)
            {
                continue;
            }

            card.UpdateSizesAndOffsets();
            if (Mathf.Abs(card.PosYStartEnd.y - card.PosYStartEnd.x) < 1f)
            {
                card.PosYStartEnd = new Vector2(0f, 48f);
            }

            string sightName = RowNames[i];
            if (TryAdjust(sightName, out int kind, out int direction))
            {
                BindAdjust(card, kind, direction, slot, key);
            }
            else if (TryCommit(sightName, out bool saving))
            {
                BindCommit(card, saving, slot, key);
            }
            else
            {
                BindRow(card, sightName, string.Equals(sightName, current, StringComparison.Ordinal), slot, key);
            }
        }

        ShowInfo(current);

        if (scroll.HasValue)
        {
            RestoreScroll(window, scroll.Value);
        }

        window.UpdateContentItemVisibility();
        bar.m_popupVisible = true;
        bar.Select();
        bar.ShowPopup();
        window.SelectHeader(key);
    }

    private static string[] OrderedSights()
    {
        string[] sights = ScopeCatalog.SightNames;
        string fallback = null;
        List<string> scopes = new List<string>(sights.Length);
        for (int i = 0; i < sights.Length; i++)
        {
            string sightName = sights[i];
            if (fallback == null && IsMenuDefault(sightName))
            {
                fallback = sightName;
                continue;
            }

            if (ScopeCatalog.IsUnusableScope(sightName) || ScopeCatalog.IsUnusableScope(ScopeCatalog.Title(sightName)))
            {
                continue;
            }

            scopes.Add(sightName);
        }

        scopes.Sort(CompareSights);
        if (fallback != null)
        {
            scopes.Insert(0, fallback);
        }

        return scopes.ToArray();
    }

    private static bool IsMenuDefault(string sightName)
    {
        return string.IsNullOrEmpty(sightName) || string.Equals(sightName, GearJsonEditor.DefaultSight, StringComparison.Ordinal);
    }

    private static int CompareSights(string left, string right)
    {
        int byTitle = CompareTitle(ScopeCatalog.Title(left), ScopeCatalog.Title(right));
        return byTitle != 0 ? byTitle : string.Compare(left, right, StringComparison.Ordinal);
    }

    private static int CompareTitle(string left, string right)
    {
        left ??= string.Empty;
        right ??= string.Empty;
        int i = 0;
        int j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                int leftEnd = i;
                int rightEnd = j;
                while (leftEnd < left.Length && char.IsDigit(left[leftEnd]))
                {
                    leftEnd++;
                }

                while (rightEnd < right.Length && char.IsDigit(right[rightEnd]))
                {
                    rightEnd++;
                }

                int byNumber = CompareNumber(left, i, leftEnd, right, j, rightEnd);
                if (byNumber != 0)
                {
                    return byNumber;
                }

                i = leftEnd;
                j = rightEnd;
                continue;
            }

            int byLetter = char.ToUpperInvariant(left[i]) - char.ToUpperInvariant(right[j]);
            if (byLetter != 0)
            {
                return byLetter;
            }

            i++;
            j++;
        }

        return (left.Length - i) - (right.Length - j);
    }

    private static int CompareNumber(string left, int leftStart, int leftEnd, string right, int rightStart, int rightEnd)
    {
        while (leftStart < leftEnd && left[leftStart] == '0')
        {
            leftStart++;
        }

        while (rightStart < rightEnd && right[rightStart] == '0')
        {
            rightStart++;
        }

        int leftCount = leftEnd - leftStart;
        int rightCount = rightEnd - rightStart;
        if (leftCount != rightCount)
        {
            return leftCount - rightCount;
        }

        for (int i = 0; i < leftCount; i++)
        {
            int difference = left[leftStart + i] - right[rightStart + i];
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    }

    private static void AddContent(Il2CppSystem.Collections.Generic.List<iScrollWindowContent> contents, CM_PlayerLobbyBar bar, string sightName, bool selected)
    {
        CM_LobbyScrollItem card = CreateCard(bar, sightName, selected);
        if (card != null)
        {
            contents.Add(new iScrollWindowContent(card.Pointer));
        }
    }

    private static bool TryAdjust(string sightName, out int kind, out int direction)
    {
        kind = 0;
        direction = 0;
        if (string.Equals(sightName, ScopeUpId, StringComparison.Ordinal))
        {
            direction = 1;
            return true;
        }

        if (string.Equals(sightName, ScopeDownId, StringComparison.Ordinal))
        {
            direction = -1;
            return true;
        }

        if (string.Equals(sightName, ForwardId, StringComparison.Ordinal))
        {
            kind = 1;
            direction = 1;
            return true;
        }

        if (string.Equals(sightName, BackId, StringComparison.Ordinal))
        {
            kind = 1;
            direction = -1;
            return true;
        }

        return false;
    }

    private static bool TryCommit(string sightName, out bool save)
    {
        if (string.Equals(sightName, SaveId, StringComparison.Ordinal))
        {
            save = true;
            return true;
        }

        if (string.Equals(sightName, ResetId, StringComparison.Ordinal))
        {
            save = false;
            return true;
        }

        save = false;
        return false;
    }

    private static void BindAdjust(CM_LobbyScrollItem card, int kind, int direction, InventorySlot slot, int key)
    {
        card.IsSelected = false;
        card.IsDimmed = false;
        Transform background = card.transform.Find("Background");
        if (background != null)
        {
            background.gameObject.SetActive(false);
        }

        StampRow(card, ScopeAlignment.RowLabel(kind, direction, slot, ScopeChoices.Display(slot)), false, Accent);
        FitCardHitbox(card);
        int capturedKind = kind;
        int capturedDirection = direction;
        InventorySlot capturedSlot = slot;
        int capturedKey = key;
        card.SetOnBtnPressCallback(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(_ => NudgeRow(capturedSlot, capturedKey, capturedKind, capturedDirection))));
        card.OnBtnHoverChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int, bool>>((Action<int, bool>)((_, _) =>
        {
            if (OpenBar == null || OpenSlot != capturedSlot)
            {
                return;
            }

            ShowInfo(ScopeChoices.Display(capturedSlot));
        }));
        card.m_inIntro = false;
        card.SetButtonEnabled(true);
    }

    private static void NudgeRow(InventorySlot slot, int key, int kind, int direction)
    {
        if (!ScopeAlignment.Nudge(slot, ScopeChoices.Display(slot), kind, direction))
        {
            return;
        }

        CM_ScrollWindow window = OpenBar != null ? OpenBar.m_popupScrollWindow : null;
        float scroll = window != null ? window.m_posOffset : 0f;
        Draw(slot, key, scroll);
    }

    private static void BindCommit(CM_LobbyScrollItem card, bool save, InventorySlot slot, int key)
    {
        card.IsSelected = false;
        card.IsDimmed = false;
        Transform background = card.transform.Find("Background");
        if (background != null)
        {
            background.gameObject.SetActive(false);
        }

        string sightName = ScopeChoices.Display(slot);
        StampRow(card, save ? ScopeAlignment.SaveLabel(slot, sightName) : ScopeAlignment.ResetLabel(), false, save ? SaveColor : ResetColor);
        FitCardHitbox(card);
        bool capturedSave = save;
        InventorySlot capturedSlot = slot;
        int capturedKey = key;
        card.SetOnBtnPressCallback(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(_ => CommitRow(capturedSlot, capturedKey, capturedSave))));
        card.OnBtnHoverChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int, bool>>((Action<int, bool>)((_, _) =>
        {
            if (OpenBar == null || OpenSlot != capturedSlot)
            {
                return;
            }

            ShowInfo(ScopeChoices.Display(capturedSlot));
        }));
        card.m_inIntro = false;
        card.SetButtonEnabled(true);
    }

    private static void CommitRow(InventorySlot slot, int key, bool save)
    {
        string sightName = ScopeChoices.Display(slot);
        bool changed = save ? ScopeAlignment.Save(slot, sightName) : ScopeAlignment.Reset(slot, sightName);
        if (!changed)
        {
            return;
        }

        CM_ScrollWindow window = OpenBar != null ? OpenBar.m_popupScrollWindow : null;
        float scroll = window != null ? window.m_posOffset : 0f;
        Draw(slot, key, scroll);
    }

    private static CM_LobbyScrollItem CreateCard(CM_PlayerLobbyBar bar, string sightName, bool selected)
    {
        GameObject cardObject = UnityEngine.Object.Instantiate(bar.m_clothesCardPrefab, bar.transform);
        cardObject.name = CardName;
        Cards.Add(cardObject);
        RowNames.Add(sightName);
        CM_LobbyScrollItem card = cardObject.GetComponent<CM_LobbyScrollItem>();
        if (card == null)
        {
            Cards.Remove(cardObject);
            RowNames.RemoveAt(RowNames.Count - 1);
            UnityEngine.Object.Destroy(cardObject);
            return null;
        }

        Transform textRoot = bar.m_parentPage != null ? bar.m_parentPage.transform : bar.transform;
        card.TextMeshRoot = textRoot;
        try
        {
            card.SetupFromLobby(bar.transform, bar, false);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Scope row setup: " + ex.Message);
        }

        card.ForcePopupLayer(true, null);
        card.transform.localScale = Vector3.one;
        if (!TryAdjust(sightName, out _, out _) && !TryCommit(sightName, out _))
        {
            card.PosYStartEnd = new Vector2(0f, 72f);
        }
        RemoveLocalizers(cardObject);
        card.m_useInfoPopup = false;
        card.m_useHover = true;
        card.m_alphaSpriteOnHover = true;
        card.m_alphaTextOnHover = false;
        card.m_textAlphaOut = 1f;
        card.m_textAlphaOver = 1f;
        card.SetButtonEnabled(true);
        card.SetEmpty(false);
        card.IsSelected = selected;
        card.m_inIntro = false;
        if (card.m_emptyIcon != null)
        {
            card.m_emptyIcon.gameObject.SetActive(false);
        }

        if (card.m_icon != null)
        {
            card.m_icon.enabled = false;
        }

        Transform background = card.transform.Find("Background");
        if (background != null)
        {
            background.gameObject.SetActive(selected);
        }

        card.StopIntro();
        card.SetVisible(true);
        return card;
    }

    private static void BindRow(CM_LobbyScrollItem card, string sightName, bool selected, InventorySlot slot, int key)
    {
        card.IsSelected = selected;
        card.IsDimmed = false;
        Transform background = card.transform.Find("Background");
        if (background != null)
        {
            background.gameObject.SetActive(selected);
        }

        StampRow(card, ScopeCatalog.MenuLabel(sightName), selected, null, ScopeCatalog.Detail(sightName));
        FitCardHitbox(card, 72f);
        string capturedName = sightName;
        InventorySlot capturedSlot = slot;
        int capturedKey = key;
        card.SetOnBtnPressCallback(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(_ => Choose(capturedSlot, capturedKey, capturedName))));
        card.OnBtnHoverChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int, bool>>((Action<int, bool>)((_, over) =>
        {
            if (OpenBar == null || OpenSlot != capturedSlot)
            {
                return;
            }

            ShowInfo(over ? capturedName : ScopeChoices.Display(capturedSlot));
        }));
        card.m_inIntro = false;
        card.SetButtonEnabled(true);
    }

    private static void StampRow(CM_LobbyScrollItem card, string title, bool selected, Color? tint)
    {
        StampRow(card, title, selected, tint, null);
    }

    private static void StampRow(CM_LobbyScrollItem card, string title, bool selected, Color? tint, string detail)
    {
        KeepTextOpaque(card);
        TextMeshPro label = card.m_subTitleText != null ? card.m_subTitleText : card.m_nameText;
        bool twoLines = !string.IsNullOrEmpty(detail);
        if (label != null)
        {
            label.gameObject.SetActive(true);
            label.enabled = true;
            label.richText = false;
            label.enableWordWrapping = false;
            label.overflowMode = twoLines ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            string heading = selected ? title + "    SELECTED" : title;
            label.text = twoLines ? heading + "\n" + detail : heading;
            label.color = tint ?? (selected ? SelectedColor : Color.white);
            label.alpha = 1f;
            if (twoLines)
            {
                FitTwoLines(label);
            }

            label.ForceMeshUpdate();
        }

        HideExtraText(card.m_nameText, label);
        HideExtraText(card.m_descText, label);
    }

    private static void FitTwoLines(TextMeshPro label)
    {
        RectTransform rect = label.rectTransform;
        if (rect == null)
        {
            return;
        }

        float line = label.fontSize > 1f ? label.fontSize : 20f;
        float needed = line * 2.4f;
        if (rect.rect.height >= needed)
        {
            return;
        }

        float grow = needed - rect.rect.height;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, needed);
        Vector2 anchored = rect.anchoredPosition;
        anchored.y += grow * 0.5f;
        rect.anchoredPosition = anchored;
    }

    private static void KeepTextOpaque(CM_LobbyScrollItem card)
    {
        card.m_alphaTextOnHover = false;
        card.m_textAlphaOut = 1f;
        card.m_textAlphaOver = 1f;
        Paint(card.m_textColorOrg);
        Paint(card.m_textColorOver);
        Paint(card.m_textColorOut);
    }

    private static void Paint(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color> colors)
    {
        if (colors == null)
        {
            return;
        }

        for (int i = 0; i < colors.Length; i++)
        {
            Color color = colors[i];
            color.a = 1f;
            if (color.maxColorComponent < 0.15f)
            {
                color = Color.white;
            }

            colors[i] = color;
        }
    }

    private static void HideExtraText(TextMeshPro text, TextMeshPro keep)
    {
        if (text == null || (keep != null && text.Pointer == keep.Pointer))
        {
            return;
        }

        text.text = string.Empty;
        text.alpha = 0f;
        text.gameObject.SetActive(false);
    }

    private static void FitCardHitbox(CM_LobbyScrollItem card)
    {
        FitCardHitbox(card, 48f);
    }

    private static void FitCardHitbox(CM_LobbyScrollItem card, float minHeight)
    {
        RectTransform rect = card.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        Vector2 size = card.GetSize();
        Rect local = rect.rect;
        float width = Mathf.Max(size.x, Mathf.Abs(local.width));
        float height = Mathf.Max(size.y, Mathf.Abs(local.height), minHeight);
        if (width < 8f)
        {
            width = 640f;
        }

        if (height < 8f)
        {
            height = minHeight;
        }

        AlignCollider(card, rect, width, height);
        if (card.m_collider != null)
        {
            card.m_collider.enabled = true;
        }

        if (card.m_slotCollider is BoxCollider2D slotCollider)
        {
            slotCollider.enabled = true;
            slotCollider.size = new Vector2(width, height);
            Vector3 center = slotCollider.transform.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            slotCollider.offset = new Vector2(center.x, center.y);
        }
    }

    private static void Choose(InventorySlot slot, int key, string sightName)
    {
        ScopeChoices.Set(slot, sightName);
        ScopeSight.ApplySelection(slot);
        Plugin.LogSource.LogInfo((slot == InventorySlot.GearSpecial ? "Special" : "Primary") + " sight set to " + sightName + ".");
        CM_ScrollWindow window = OpenBar != null ? OpenBar.m_popupScrollWindow : null;
        float scroll = window != null ? window.m_posOffset : 0f;
        Draw(slot, key, scroll);
    }

    private static void ShowInfo(string sightName)
    {
        CM_ScrollWindow window = OpenBar != null ? OpenBar.m_popupScrollWindow : null;
        CM_ScrollWindowInfoBox box = window != null ? window.InfoBox : null;
        if (box == null)
        {
            return;
        }

        bool selected = string.Equals(sightName, ScopeChoices.Display(OpenSlot), StringComparison.Ordinal);
        string title = ScopeCatalog.Title(sightName);
        string description = ScopeCatalog.Describe(sightName, OpenSlot, selected) + "\n\n" + ScopeAlignment.Summary(OpenSlot, sightName, selected);
        try
        {
            box.SetInfoBox(title, string.Empty, description, string.Empty, string.Empty, null);
            if (box.m_infoMainTitleText != null)
            {
                box.m_infoMainTitleText.gameObject.SetActive(true);
                box.m_infoMainTitleText.text = title;
                box.m_infoMainTitleText.color = Color.white;
                box.m_infoMainTitleText.alpha = 1f;
                box.m_infoMainTitleText.alignment = TextAlignmentOptions.Center;
                box.m_infoMainTitleText.enableWordWrapping = true;
            }

            if (box.m_infoDescriptionText != null)
            {
                box.m_infoDescriptionText.gameObject.SetActive(true);
                box.m_infoDescriptionText.text = description;
                box.m_infoDescriptionText.color = new Color(0.82f, 0.86f, 0.9f, 1f);
                box.m_infoDescriptionText.alpha = 1f;
                box.m_infoDescriptionText.alignment = TextAlignmentOptions.TopLeft;
                box.m_infoDescriptionText.enableWordWrapping = true;
                box.m_infoDescriptionText.overflowMode = TextOverflowModes.Overflow;
            }

            IntPtr titlePointer = box.m_infoMainTitleText != null ? box.m_infoMainTitleText.Pointer : IntPtr.Zero;
            IntPtr descriptionPointer = box.m_infoDescriptionText != null ? box.m_infoDescriptionText.Pointer : IntPtr.Zero;
            TextMeshPro[] texts = box.GetComponentsInChildren<TextMeshPro>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TextMeshPro text = texts[i];
                if (text == null || text.Pointer == titlePointer || text.Pointer == descriptionPointer)
                {
                    continue;
                }

                text.text = string.Empty;
                text.alpha = 0f;
            }

            if (box.m_infoAcceptButton != null)
            {
                box.m_infoAcceptButton.gameObject.SetActive(false);
            }

            if (box.m_infoRejectButton != null)
            {
                box.m_infoRejectButton.gameObject.SetActive(false);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Scope info box: " + ex.Message);
        }
    }

    private static void RestoreScroll(CM_ScrollWindow window, float posOffset)
    {
        if (window.m_scrollBar == null || !window.m_scrollBar.Visible || window.m_contentContainer == null)
        {
            return;
        }

        float range = window.m_contentContainerHeight - window.m_windowHeight;
        window.m_posOffset = Mathf.Clamp(posOffset, 0f, Mathf.Max(0f, range));
        Vector2 position = window.m_contentContainer.GetPosition();
        position.y = window.m_posStart + window.m_posOffset;
        window.m_contentContainer.SetPosition(position);
        window.m_scrollBar.SetHandlePosition(range <= 0f ? 0f : Mathf.Clamp01(window.m_posOffset / range), window.m_windowHeight);
    }

    private static void EnterPopupFocus()
    {
        FocusStateManager.EnterMenu((eCM_MenuPage)13, false);
        eGameStateName state = GameStateManager.CurrentStateName;
        if (((int)state == 6 || (int)state == 10) && FocusStateManager.Current != null)
        {
            FocusStateManager.Current.m_previousToggleState = (eFocusState)4;
        }
    }

    private static void PlacePopup(CM_PlayerLobbyBar bar)
    {
        Transform align = null;
        if (CM_PageLoadout.Current != null)
        {
            align = CM_PageLoadout.Current.m_popupAlign;
        }

        if (align == null)
        {
            align = bar.m_playerSlotIndex <= 1 ? bar.m_popupScrollWindowAlign_Right : bar.m_popupScrollWindowAlign_Left;
        }

        if (bar.m_popupHolder != null)
        {
            bar.m_popupHolder.gameObject.SetActive(true);
            if (align != null)
            {
                bar.m_popupHolder.position = align.position + new Vector3(300f, 0f, 0f);
            }
        }

        if (bar.m_popupItemHolder != null)
        {
            bar.m_popupItemHolder.gameObject.SetActive(true);
        }
    }

    private static CM_ScrollWindow EnsureWindow(CM_PlayerLobbyBar bar)
    {
        if (bar.m_popupScrollWindow != null)
        {
            return bar.m_popupScrollWindow;
        }

        if (bar.m_popupScrollWindowPrefab == null)
        {
            return null;
        }

        Transform parent = bar.m_popupHolder != null ? bar.m_popupHolder : bar.transform;
        GameObject windowObject = UnityEngine.Object.Instantiate(bar.m_popupScrollWindowPrefab, parent);
        CM_ScrollWindow window = windowObject.GetComponent<CM_ScrollWindow>();
        if (window == null)
        {
            UnityEngine.Object.Destroy(windowObject);
            return null;
        }

        try
        {
            window.Setup(parent);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug("Scope popup setup: " + ex.Message);
        }

        bar.m_popupScrollWindow = window;
        return window;
    }

    private static void LabelButton(CM_LobbyScrollItem button)
    {
        TextMeshPro primary = button.m_nameText;
        if (primary == null)
        {
            primary = button.GetComponentInChildren<TextMeshPro>(true);
        }

        TextMeshPro[] texts = button.GetComponentsInChildren<TextMeshPro>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TextMeshPro text = texts[i];
            if (text == null)
            {
                continue;
            }

            if (text == primary)
            {
                text.gameObject.SetActive(true);
                text.text = "SCOPES";
                text.color = Accent;
                text.alignment = TextAlignmentOptions.Center;
                RectTransform textRect = text.GetComponent<RectTransform>();
                if (textRect != null)
                {
                    textRect.anchorMin = Vector2.zero;
                    textRect.anchorMax = Vector2.one;
                    textRect.offsetMin = Vector2.zero;
                    textRect.offsetMax = Vector2.zero;
                    textRect.pivot = new Vector2(0.5f, 0.5f);
                }
            }
            else
            {
                text.text = string.Empty;
            }
        }
    }

    private static void RemoveLocalizers(GameObject target)
    {
        TMP_Localizer[] localizers = target.GetComponentsInChildren<TMP_Localizer>(true);
        for (int i = 0; i < localizers.Length; i++)
        {
            if (localizers[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(localizers[i]);
            }
        }
    }

    private static void FitButton(CM_LobbyScrollItem item, float width, float height)
    {
        RectTransform rect = item.GetComponent<RectTransform>();
        if (rect == null || width < 8f)
        {
            return;
        }

        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        if (height > 1f)
        {
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        float fittedHeight = height > 1f ? height : rect.sizeDelta.y;
        Transform topLine = FindChild(rect, "StretchLineT");
        Transform bottomLine = FindChild(rect, "StretchLineB");
        Transform leftLine = FindChild(rect, "StretchLineL");
        Transform rightLine = FindChild(rect, "StretchLineR");
        if (topLine == null || bottomLine == null || leftLine == null || rightLine == null || topLine.parent == null)
        {
            AlignCollider(item, rect, width, fittedHeight);
            return;
        }

        RectTransform box = topLine.parent.GetComponent<RectTransform>();
        RectTransform top = topLine.GetComponent<RectTransform>();
        RectTransform bottom = bottomLine.GetComponent<RectTransform>();
        RectTransform left = leftLine.GetComponent<RectTransform>();
        RectTransform right = rightLine.GetComponent<RectTransform>();
        Transform background = FindChild(box, "Background");
        RectTransform backgroundRect = background != null ? background.GetComponent<RectTransform>() : null;
        if (box == null || top == null || bottom == null || left == null || right == null)
        {
            AlignCollider(item, rect, width, fittedHeight);
            return;
        }

        Vector2 size = new Vector2(width, fittedHeight);
        box.sizeDelta = size;
        if (backgroundRect != null)
        {
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
        }

        top.anchorMin = new Vector2(0f, 1f);
        top.anchorMax = new Vector2(1f, 1f);
        top.offsetMin = new Vector2(0f, -4f);
        top.offsetMax = Vector2.zero;
        bottom.anchorMin = Vector2.zero;
        bottom.anchorMax = new Vector2(1f, 0f);
        bottom.offsetMin = new Vector2(0f, -4f);
        bottom.offsetMax = Vector2.zero;
        left.anchorMin = Vector2.zero;
        left.anchorMax = new Vector2(0f, 1f);
        left.offsetMin = Vector2.zero;
        left.offsetMax = new Vector2(12f, 0f);
        right.anchorMin = new Vector2(1f, 0f);
        right.anchorMax = Vector2.one;
        right.offsetMin = Vector2.zero;
        right.offsetMax = new Vector2(4f, 0f);
        UI_SpriteResizer[] resizers = item.GetComponentsInChildren<UI_SpriteResizer>(true);
        for (int i = 0; i < resizers.Length; i++)
        {
            if (resizers[i] != null)
            {
                resizers[i].Resize();
            }
        }

        AlignCollider(item, rect, width, fittedHeight);
    }

    private static void AlignCollider(CM_LobbyScrollItem item, RectTransform rect, float width, float height)
    {
        if (item.m_collider == null)
        {
            return;
        }

        item.m_collider.size = new Vector2(width, height);
        Vector3 center = item.m_collider.transform.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
        item.m_collider.offset = new Vector2(center.x, center.y);
    }

    private static Transform FindChild(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        if (root.name == childName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChild(root.GetChild(i), childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void DestroyCards()
    {
        for (int i = 0; i < Cards.Count; i++)
        {
            GameObject card = Cards[i];
            if (card != null)
            {
                UnityEngine.Object.Destroy(card);
            }
        }

        Cards.Clear();
        RowNames.Clear();
    }

    private static CM_LobbyScrollItem ButtonFor(CM_PlayerLobbyBar bar)
    {
        if (bar == null || !Buttons.TryGetValue(bar.Pointer, out CM_LobbyScrollItem button))
        {
            return null;
        }

        if (button == null)
        {
            Buttons.Remove(bar.Pointer);
            ButtonBars.Remove(bar.Pointer);
            return null;
        }

        return button;
    }

    internal static void ReleaseDestroyed()
    {
        int frame = Time.frameCount;
        if (frame == ReleasedFrame)
        {
            return;
        }

        ReleasedFrame = frame;
        if (OpenBar == null)
        {
            OpenBar = null;
            if (Cards.Count > 0)
            {
                DestroyCards();
            }
        }

        List<IntPtr> drop = null;
        foreach (KeyValuePair<IntPtr, CM_PlayerLobbyBar> pair in ButtonBars)
        {
            if (pair.Value != null)
            {
                continue;
            }

            if (Buttons.TryGetValue(pair.Key, out CM_LobbyScrollItem button) && button != null)
            {
                UnityEngine.Object.Destroy(button.gameObject);
            }

            drop ??= new List<IntPtr>();
            drop.Add(pair.Key);
        }

        if (drop == null)
        {
            return;
        }

        for (int i = 0; i < drop.Count; i++)
        {
            Buttons.Remove(drop[i]);
            ButtonBars.Remove(drop[i]);
        }
    }

    internal static bool TryEditing(out InventorySlot slot)
    {
        slot = OpenSlot;
        CM_ScrollWindow window = OpenBar != null ? OpenBar.m_popupScrollWindow : null;
        return window != null && window.gameObject.activeInHierarchy && OpenBar.m_popupVisible;
    }

    private static bool IsLocal(CM_PlayerLobbyBar bar)
    {
        SNet_Player player = bar != null ? bar.m_player : null;
        return player != null && player.IsLocal;
    }
}
