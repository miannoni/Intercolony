using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public class Designator_ProducePreset : Designator_Cells
    {
        private const string DefaultDescription =
            "Dragging applies this preset's production settings to every eligible object touched.";
        private const string UnknownFailureReason = "unknown reason";
        private const string AppliedMessagePrefix = "Applied \"";
        private const string AppliedMessageSeparator = "\" to ";
        private const string SingularObject = "object";
        private const string PluralObjects = "objects";
        private const string MessageSpace = " ";
        private const string MessagePeriod = ".";
        private const string FailureListSeparator = "; ";
        private const string SkippedMessageSeparator = " skipped: ";
        private const string EditLabel = "Edit";
        private const string RenameLabel = "Rename";
        private const string RemoveLabel = "Remove";
        private const string RemoveConfirmationText =
            "Remove this preset?\n\nRemoving it deletes the Architect entry but does not stop or change any production loop already configured from it.";

        private readonly ProduceControlPreset preset;
        private readonly int presetId;

        public Designator_ProducePreset(ProduceControlPreset preset)
        {
            this.preset = preset;
            presetId = preset == null ? 0 : preset.id;
            defaultLabel = preset == null ? string.Empty : preset.name;
            defaultDesc = DefaultDescription;
            icon = ContentFinder<Texture2D>.Get("UI/Designators/Uninstall");
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
        }

        public int PresetId => presetId;

        public override string Label => preset == null || preset.name == null ? string.Empty : preset.name;

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                Map currentMap = base.Map;
                if (currentMap == null)
                {
                    // The inherited designator menu can inspect the map, so skip it during map teardown.
                    yield break;
                }

                IEnumerable<FloatMenuOption> inheritedOptions = base.RightClickFloatMenuOptions;
                if (inheritedOptions != null)
                {
                    foreach (FloatMenuOption option in inheritedOptions)
                    {
                        yield return option;
                    }
                }

                ProduceLoopMapComponent loopComponent = ProduceLoopMapComponent.For(currentMap);
                ProduceControlPreset currentPreset = loopComponent == null
                    ? null
                    : loopComponent.FindPreset(PresetId);
                if (loopComponent == null || currentPreset == null)
                {
                    yield break;
                }

                yield return new FloatMenuOption(
                    EditLabel,
                    () => OpenEditDialog(currentMap));
                yield return new FloatMenuOption(
                    RenameLabel,
                    () => OpenRenameDialog(currentMap));
                yield return new FloatMenuOption(
                    RemoveLabel,
                    () => ConfirmRemovePreset(currentMap));
            }
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            Map currentMap = base.Map;
            if (currentMap == null || preset == null)
            {
                return false;
            }

            ProduceLoopMapComponent loopComponent = ProduceLoopMapComponent.For(currentMap);
            if (loopComponent == null)
            {
                return false;
            }

            IntVec3 targetCell;
            // Material compatibility belongs to TryApplyPreset so incompatible objects remain countable in the batch summary.
            return loopComponent.TryResolveProduceTargetCell(c, out targetCell);
        }

        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            ApplyPresetToCells(cells);
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            DesignateMultiCell(new IntVec3[] { c });
        }

        private void ApplyPresetToCells(IEnumerable<IntVec3> cells)
        {
            HashSet<IntVec3> distinctTargetCells = new HashSet<IntVec3>();
            List<IntVec3> orderedTargetCells = new List<IntVec3>();
            Map currentMap = base.Map;
            ProduceLoopMapComponent loopComponent = currentMap == null
                ? null
                : ProduceLoopMapComponent.For(currentMap);

            if (loopComponent != null && cells != null)
            {
                foreach (IntVec3 cell in cells)
                {
                    IntVec3 targetCell;
                    // A multi-cell object must be applied once, regardless of how many drag cells cover it.
                    if (loopComponent.TryResolveProduceTargetCell(cell, out targetCell) &&
                        distinctTargetCells.Add(targetCell))
                    {
                        orderedTargetCells.Add(targetCell);
                    }
                }
            }

            int applied = 0;
            int skipped = 0;
            Dictionary<string, int> failures = new Dictionary<string, int>();
            if (loopComponent != null)
            {
                for (int i = 0; i < orderedTargetCells.Count; i++)
                {
                    IntVec3 ignoredTargetCell;
                    string reason;
                    // TryApplyPreset owns the material decision and supplies the reason shown to the player.
                    if (loopComponent.TryApplyPreset(
                            orderedTargetCells[i],
                            preset,
                            out ignoredTargetCell,
                            out reason))
                    {
                        applied++;
                        continue;
                    }

                    skipped++;
                    string failureReason = reason ?? UnknownFailureReason;
                    int failureCount;
                    if (failures.TryGetValue(failureReason, out failureCount))
                    {
                        failures[failureReason] = failureCount + 1;
                    }
                    else
                    {
                        failures.Add(failureReason, 1);
                    }
                }
            }

            // The base finalization preserves the normal success/failure sound; the batch gets one summary below.
            Finalize(applied > 0);
            if (orderedTargetCells.Count == 0)
            {
                return;
            }

            string objectWord = applied == 1 ? SingularObject : PluralObjects;
            string message = AppliedMessagePrefix + Label + AppliedMessageSeparator +
                applied + MessageSpace + objectWord + MessagePeriod;
            if (skipped > 0)
            {
                List<string> sortedReasons = new List<string>(failures.Keys);
                sortedReasons.Sort(StringComparer.Ordinal);
                message += MessageSpace + skipped + SkippedMessageSeparator +
                    string.Join(FailureListSeparator, sortedReasons) + MessagePeriod;
            }

            MessageTypeDef messageType = applied > 0
                ? MessageTypeDefOf.TaskCompletion
                : MessageTypeDefOf.RejectInput;
            Messages.Message(message, messageType, historical: false);
        }

        private void OpenEditDialog(Map currentMap)
        {
            ProduceLoopMapComponent loopComponent = currentMap == null
                ? null
                : ProduceLoopMapComponent.For(currentMap);
            ProduceControlPreset currentPreset = loopComponent == null
                ? null
                : loopComponent.FindPreset(PresetId);
            if (currentMap == null || loopComponent == null || currentPreset == null ||
                Find.WindowStack == null)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_EditProducePreset(currentMap, PresetId));
        }

        private void OpenRenameDialog(Map currentMap)
        {
            ProduceLoopMapComponent loopComponent = currentMap == null
                ? null
                : ProduceLoopMapComponent.For(currentMap);
            ProduceControlPreset currentPreset = loopComponent == null
                ? null
                : loopComponent.FindPreset(PresetId);
            if (currentMap == null || loopComponent == null || currentPreset == null ||
                Find.WindowStack == null)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_RenameProducePreset(currentMap, PresetId));
        }

        private void ConfirmRemovePreset(Map currentMap)
        {
            ProduceLoopMapComponent loopComponent = currentMap == null
                ? null
                : ProduceLoopMapComponent.For(currentMap);
            ProduceControlPreset currentPreset = loopComponent == null
                ? null
                : loopComponent.FindPreset(PresetId);
            if (currentMap == null || loopComponent == null || currentPreset == null ||
                Find.WindowStack == null)
            {
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                RemoveConfirmationText,
                () => RemovePresetFromMap(currentMap),
                destructive: true));
        }

        private void RemovePresetFromMap(Map currentMap)
        {
            ProduceLoopMapComponent loopComponent = currentMap == null
                ? null
                : ProduceLoopMapComponent.For(currentMap);
            ProduceControlPreset currentPreset = loopComponent == null
                ? null
                : loopComponent.FindPreset(PresetId);
            if (currentMap == null || loopComponent == null || currentPreset == null)
            {
                return;
            }

            loopComponent.RemovePreset(PresetId);
        }
    }
}
