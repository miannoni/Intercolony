using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public class Dialog_ProducePresetManager : Window
    {
        private const float WindowWidth = 720f;
        private const float WindowMargin = 18f;
        private const float MaxScreenHeightFraction = 0.95f;
        private const float BottomButtonsHeight = 40f;
        private const float ContentInset = 8f;
        private const float ScrollbarGutter = 16f;
        private const float ContentLeft = ContentInset + ScrollbarGutter;
        private const float RowHeight = 32f;
        private const float RowGap = 6f;
        private const float NameActionGap = 12f;
        private const float ActionButtonWidth = 76f;
        private const float ActionButtonGap = 4f;
        private const float ButtonVerticalPadding = 8f;
        private const float CloseButtonWidth = 120f;

        private const string Title = "Produce controls";
        private const string NewProductionLabel = "New production";
        private const string ApplyLabel = "Apply";
        private const string EditLabel = "Edit";
        private const string RenameLabel = "Rename";
        private const string RemoveLabel = "Remove";
        private const string CloseLabel = "Close";
        private const string NewProductionTooltip =
            "Open the production editor for this object.";
        private const string NewProductionUnavailableTooltip =
            "A production is created on a specific object. Open Produce controls from the object you want to configure.";
        private const string EmptyPresetMessage =
            "Presets are created from an object's Produce Controls popup with Save as preset.";
        private const string UnnamedPresetLabel = "Unnamed preset";
        private const string ApplyTooltip =
            "Select this preset's Architect drag designator and paint it onto eligible objects.";
        private const string EditTooltip = "Edit this preset's saved production settings.";
        private const string RenameTooltip = "Rename this preset without changing its settings.";
        private const string RemoveTooltip =
            "Remove this Architect preset; existing production loops are unaffected.";
        private const string RemoveConfirmationText =
            "Remove this preset?\n\nRemoving it deletes the Architect entry but does not stop or change any production loop already configured from it.";
        private const string ApplyUnavailableMessage =
            "This preset is not available for painting right now.";

        private readonly Map map;
        private readonly IntVec3? contextCell;
        private Vector2 presetsScroll;

        public Dialog_ProducePresetManager(Map map)
            : this(map, null)
        {
        }

        public Dialog_ProducePresetManager(Map map, IntVec3? contextCell)
        {
            this.map = map;
            this.contextCell = contextCell;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                Text.Font = GameFont.Small;
                float contentWidth = ContentWidth(WindowWidth - WindowMargin * 2f);
                float fixedHeight = WindowMargin * 2f + BottomButtonsHeight;
                ProduceLoopMapComponent component = map == null
                    ? null
                    : ProduceLoopMapComponent.For(map);
                IReadOnlyList<ProduceControlPreset> presets = component == null
                    ? null
                    : component.Presets;
                float contentHeight = ContentHeight(contentWidth, presets);
                float height = Mathf.Min(fixedHeight + contentHeight,
                    UI.screenHeight * MaxScreenHeightFraction);
                return new Vector2(WindowWidth,
                    Mathf.Max(fixedHeight + Text.LineHeight, height));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            ProduceLoopMapComponent component = map == null
                ? null
                : ProduceLoopMapComponent.For(map);
            if (map == null || component == null)
            {
                // The map component can disappear during map teardown while this window is open.
                if (Find.WindowStack != null)
                {
                    Close();
                }
                return;
            }

            IReadOnlyList<ProduceControlPreset> presets = component.Presets;
            float contentWidth = ContentWidth(inRect.width);
            float bottom = inRect.height - BottomButtonsHeight;
            Rect presetsRect = new Rect(
                ContentLeft,
                0f,
                contentWidth + ScrollbarGutter,
                Mathf.Max(1f, bottom));
            float contentHeight = ContentHeight(contentWidth, presets);
            if (contentHeight <= presetsRect.height)
            {
                presetsScroll = Vector2.zero;
                GUI.BeginGroup(new Rect(ContentLeft, 0f, contentWidth, presetsRect.height));
                DrawPresets(contentWidth, presets);
                GUI.EndGroup();
            }
            else
            {
                Rect presetsView = new Rect(0f, 0f, contentWidth, contentHeight);
                Widgets.BeginScrollView(presetsRect, ref presetsScroll, presetsView);
                DrawPresets(contentWidth, presets);
                Widgets.EndScrollView();
            }

            Rect closeRect = new Rect(
                ContentLeft + contentWidth - CloseButtonWidth,
                bottom,
                CloseButtonWidth,
                36f);
            if (Widgets.ButtonText(closeRect, CloseLabel))
            {
                if (Find.WindowStack != null)
                {
                    Close();
                }
            }
        }

        private void DrawPresets(float width, IReadOnlyList<ProduceControlPreset> presets)
        {
            float y = 0f;
            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight(Title, width);
            Widgets.Label(new Rect(0f, y, width, titleHeight), Title);
            y += titleHeight + RowGap;
            Text.Font = GameFont.Small;

            float newProductionHeight = NewProductionButtonHeight(width);
            Rect newProductionRect = new Rect(0f, y, width, newProductionHeight);
            bool hasContextCell = contextCell.HasValue;
            TooltipHandler.TipRegion(
                newProductionRect,
                hasContextCell ? NewProductionTooltip : NewProductionUnavailableTooltip);
            if (Widgets.ButtonText(
                    newProductionRect,
                    NewProductionLabel,
                    active: hasContextCell))
            {
                OpenNewProduction();
                return;
            }

            y += newProductionHeight + RowGap;

            if (presets == null || presets.Count == 0)
            {
                // Presets are still captured by the existing per-production editor.
                float emptyHeight = Text.CalcHeight(EmptyPresetMessage, width);
                Widgets.Label(new Rect(0f, y, width, emptyHeight), EmptyPresetMessage);
                return;
            }

            for (int i = 0; i < presets.Count; i++)
            {
                ProduceControlPreset preset = presets[i];
                float rowHeight = PresetRowHeight(width, preset);
                DrawPresetRow(width, y, rowHeight, preset);
                y += rowHeight;
                if (i < presets.Count - 1)
                {
                    y += RowGap;
                }
            }
        }

        private void OpenNewProduction()
        {
            if (!contextCell.HasValue || map == null || Find.WindowStack == null)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_ProduceControls(map, contextCell.Value));
        }

        private void DrawPresetRow(
            float width,
            float rowY,
            float rowHeight,
            ProduceControlPreset preset)
        {
            string presetName = PresetName(preset);
            float buttonAreaWidth = ActionButtonAreaWidth();
            float nameWidth = Mathf.Max(1f, width - buttonAreaWidth - NameActionGap);
            float nameHeight = Text.CalcHeight(presetName, nameWidth);
            Rect nameRect = new Rect(
                0f,
                rowY + (rowHeight - nameHeight) / 2f,
                nameWidth,
                nameHeight);
            Widgets.Label(nameRect, presetName);
            if (preset == null)
            {
                return;
            }

            float buttonHeight = ActionButtonHeight();
            float buttonY = rowY + (rowHeight - buttonHeight) / 2f;
            float buttonX = width - buttonAreaWidth;
            Rect applyRect = new Rect(buttonX, buttonY, ActionButtonWidth, buttonHeight);
            Rect editRect = new Rect(
                applyRect.xMax + ActionButtonGap,
                buttonY,
                ActionButtonWidth,
                buttonHeight);
            Rect renameRect = new Rect(
                editRect.xMax + ActionButtonGap,
                buttonY,
                ActionButtonWidth,
                buttonHeight);
            Rect removeRect = new Rect(
                renameRect.xMax + ActionButtonGap,
                buttonY,
                ActionButtonWidth,
                buttonHeight);

            TooltipHandler.TipRegion(applyRect, ApplyTooltip);
            if (Widgets.ButtonText(applyRect, ApplyLabel))
            {
                ApplyPreset(preset.id);
                return;
            }

            TooltipHandler.TipRegion(editRect, EditTooltip);
            if (Widgets.ButtonText(editRect, EditLabel))
            {
                OpenEditDialog(preset.id);
                return;
            }

            TooltipHandler.TipRegion(renameRect, RenameTooltip);
            if (Widgets.ButtonText(renameRect, RenameLabel))
            {
                OpenRenameDialog(preset.id);
                return;
            }

            TooltipHandler.TipRegion(removeRect, RemoveTooltip);
            if (Widgets.ButtonText(removeRect, RemoveLabel))
            {
                ConfirmRemovePreset(preset.id);
            }
        }

        private void ApplyPreset(int presetId)
        {
            ProduceLoopMapComponent component = map == null
                ? null
                : ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            DesignatorManager designatorManager = Find.DesignatorManager;
            WindowStack windowStack = Find.WindowStack;
            if (map == null || component == null || preset == null || designatorManager == null ||
                windowStack == null || Find.CurrentMap != map)
            {
                Messages.Message(
                    ApplyUnavailableMessage,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            ProducePresetDesignators.SyncFor(map);
            Designator_ProducePreset designator = ProducePresetDesignators.FindFor(presetId);
            if (designator == null)
            {
                Messages.Message(
                    ApplyUnavailableMessage,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            // The category instance owns the revision-synced designator used by the Architect panel.
            designatorManager.Select(designator);
            if (Find.WindowStack != null)
            {
                Close();
            }
        }

        private void OpenEditDialog(int presetId)
        {
            ProduceLoopMapComponent component = map == null
                ? null
                : ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            WindowStack windowStack = Find.WindowStack;
            if (map == null || component == null || preset == null || windowStack == null)
            {
                return;
            }

            windowStack.Add(new Dialog_EditProducePreset(map, presetId));
        }

        private void OpenRenameDialog(int presetId)
        {
            ProduceLoopMapComponent component = map == null
                ? null
                : ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            WindowStack windowStack = Find.WindowStack;
            if (map == null || component == null || preset == null || windowStack == null)
            {
                return;
            }

            windowStack.Add(new Dialog_RenameProducePreset(map, presetId));
        }

        private void ConfirmRemovePreset(int presetId)
        {
            ProduceLoopMapComponent component = map == null
                ? null
                : ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            WindowStack windowStack = Find.WindowStack;
            if (map == null || component == null || preset == null || windowStack == null)
            {
                return;
            }

            windowStack.Add(Dialog_MessageBox.CreateConfirmation(
                RemoveConfirmationText,
                () => RemovePresetFromMap(presetId),
                destructive: true));
        }

        private void RemovePresetFromMap(int presetId)
        {
            ProduceLoopMapComponent component = map == null
                ? null
                : ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            if (map == null || component == null || preset == null)
            {
                return;
            }

            component.RemovePreset(presetId);
        }

        private static float ContentWidth(float availableWidth)
        {
            return Mathf.Max(1f, availableWidth - ContentLeft * 2f);
        }

        private static float ContentHeight(
            float width,
            IReadOnlyList<ProduceControlPreset> presets)
        {
            Text.Font = GameFont.Medium;
            float height = Text.CalcHeight(Title, width) + RowGap;
            Text.Font = GameFont.Small;
            height += NewProductionButtonHeight(width) + RowGap;
            if (presets == null || presets.Count == 0)
            {
                return height + Text.CalcHeight(EmptyPresetMessage, width);
            }

            for (int i = 0; i < presets.Count; i++)
            {
                height += PresetRowHeight(width, presets[i]);
                if (i < presets.Count - 1)
                {
                    height += RowGap;
                }
            }

            return height;
        }

        private static float NewProductionButtonHeight(float width)
        {
            return Mathf.Max(
                RowHeight,
                Text.CalcHeight(NewProductionLabel, width) + ButtonVerticalPadding);
        }

        private static float PresetRowHeight(float width, ProduceControlPreset preset)
        {
            string presetName = PresetName(preset);
            float nameWidth = Mathf.Max(1f,
                width - ActionButtonAreaWidth() - NameActionGap);
            return Mathf.Max(
                RowHeight,
                Text.CalcHeight(presetName, nameWidth),
                ActionButtonHeight());
        }

        private static float ActionButtonAreaWidth()
        {
            return ActionButtonWidth * 4f + ActionButtonGap * 3f;
        }

        private static float ActionButtonHeight()
        {
            float applyHeight = Text.CalcHeight(ApplyLabel, ActionButtonWidth);
            float editHeight = Text.CalcHeight(EditLabel, ActionButtonWidth);
            float renameHeight = Text.CalcHeight(RenameLabel, ActionButtonWidth);
            float removeHeight = Text.CalcHeight(RemoveLabel, ActionButtonWidth);
            return Mathf.Max(
                RowHeight,
                applyHeight + ButtonVerticalPadding,
                editHeight + ButtonVerticalPadding,
                renameHeight + ButtonVerticalPadding,
                removeHeight + ButtonVerticalPadding);
        }

        private static string PresetName(ProduceControlPreset preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.name))
            {
                return UnnamedPresetLabel;
            }

            return preset.name;
        }
    }
}
