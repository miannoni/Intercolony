using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public class Designator_ProducePreset : Designator_Cells
    {
        private readonly ProduceControlPreset preset;
        private readonly int presetId;

        public Designator_ProducePreset(ProduceControlPreset preset)
        {
            this.preset = preset;
            presetId = preset == null ? 0 : preset.id;
            defaultLabel = preset == null ? string.Empty : preset.name;
            defaultDesc = "Dragging applies this preset's production settings to every eligible object touched.";
            icon = ContentFinder<Texture2D>.Get("UI/Designators/Uninstall");
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
        }

        public int PresetId => presetId;

        public override string Label => preset == null || preset.name == null ? string.Empty : preset.name;

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

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
                    string failureReason = reason ?? "unknown reason";
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

            string objectWord = applied == 1 ? "object" : "objects";
            string message = "Applied \"" + Label + "\" to " + applied + " " + objectWord + ".";
            if (skipped > 0)
            {
                List<string> sortedReasons = new List<string>(failures.Keys);
                sortedReasons.Sort(StringComparer.Ordinal);
                message += " " + skipped + " skipped: " + string.Join("; ", sortedReasons) + ".";
            }

            MessageTypeDef messageType = applied > 0
                ? MessageTypeDefOf.TaskCompletion
                : MessageTypeDefOf.RejectInput;
            Messages.Message(message, messageType, historical: false);
        }
    }
}
