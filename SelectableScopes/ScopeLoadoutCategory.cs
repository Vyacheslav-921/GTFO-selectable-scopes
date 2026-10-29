using BepInEx.Unity.IL2CPP;
using CellMenu;
using Il2CppInterop.Runtime;
using Player;
using SNetwork;
using TMPro;
using UnityEngine;

namespace SelectableScopes;

internal static class ScopeLoadoutCategory
{
    private const string ButtonName = "SelectableScopes.Button";

    private const string CardName = "SelectableScopes.Sight";

    private const string SkinsGuid = "io.takina.gtfo.Skins";

    private static readonly Color Accent = new Color(0.54f, 0.71f, 0.98f, 1f);

    private static readonly Dictionary<IntPtr, CM_LobbyScrollItem> Buttons = new Dictionary<IntPtr, CM_LobbyScrollItem>();

    private static readonly List<GameObject> Cards = new List<GameObject>();

    private static readonly List<string> RowNames = new List<string>();

    private static readonly Color SelectedColor = new Color(0.62f, 1f, 0.62f, 1f);

    private static CM_PlayerLobbyBar OpenBar;

    private static InventorySlot OpenSlot;

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
        float targetY = textRight.y;
        if (IL2CPPChainloader.Instance != null && IL2CPPChainloader.Instance.Plugins.ContainsKey(SkinsGuid))
        {
            targetY -= (height * scaleY) + gap;
        }

        Vector3 position = rect.position;
        position.x = textRight.x + gap;
        position.y = targetY;
        rect.position = position;
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

        Draw(InventorySlot.GearStandard, 0, null);
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
        for (int i = 0; i < ScopeCatalog.SightNames.Length; i++)
        {
            string sightName = ScopeCatalog.SightNames[i];
            CM_LobbyScrollItem card = CreateCard(bar, sightName, string.Equals(sightName, current, StringComparison.Ordinal));
            if (card == null)
            {
                continue;
            }

            contents.Add(new iScrollWindowContent(card.Pointer));
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
            BindRow(card, sightName, string.Equals(sightName, current, StringComparison.Ordinal), slot, key);
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

        StampRow(card, ScopeCatalog.Title(sightName), selected);
        FitCardHitbox(card);
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

    private static void StampRow(CM_LobbyScrollItem card, string title, bool selected)
    {
        KeepTextOpaque(card);
        TextMeshPro label = card.m_subTitleText != null ? card.m_subTitleText : card.m_nameText;
        if (label != null)
        {
            label.gameObject.SetActive(true);
            label.enabled = true;
            label.richText = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.text = selected ? title + "    SELECTED" : title;
            label.color = selected ? SelectedColor : Color.white;
            label.alpha = 1f;
            label.ForceMeshUpdate();
        }

        HideExtraText(card.m_nameText, label);
        HideExtraText(card.m_descText, label);
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
        RectTransform rect = card.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        Vector2 size = card.GetSize();
        Rect local = rect.rect;
        float width = Mathf.Max(size.x, Mathf.Abs(local.width));
        float height = Mathf.Max(size.y, Mathf.Abs(local.height));
        if (width < 8f)
        {
            width = 640f;
        }

        if (height < 8f)
        {
            height = 48f;
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
        string description = ScopeCatalog.Describe(sightName, OpenSlot, selected);
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
            return null;
        }

        return button;
    }

    private static bool IsLocal(CM_PlayerLobbyBar bar)
    {
        SNet_Player player = bar != null ? bar.m_player : null;
        return player != null && player.IsLocal;
    }
}
