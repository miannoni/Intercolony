using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Configuration for one production program. The record is resolved from the map for every draw
    /// so this window cannot keep editing a program that was disabled while it was open.
    /// </summary>
    public class Dialog_ProduceControls : Window
    {
        private const float WindowWidth = 640f;
        private const float WindowMargin = 18f;

        /// <summary>
        /// The content is measured from the rows below and capped so a future section can scroll
        /// without letting the window touch the top and bottom of the screen.
        /// </summary>
        private const float MaxScreenHeightFraction = 0.95f;
        private const float BottomButtonsHeight = 40f;
        private const float ContentInset = 8f;
        private const float ScrollbarGutter = 16f;

        /// <summary>
        /// Labels use a fixed column, and every editor begins at the same x position in the column
        /// to its right.
        /// </summary>
        private const float ContentLeft = ContentInset + ScrollbarGutter;
        private const float LabelColumnWidth = 140f;
        private const float LabelColumnGap = 12f;
        private const float RowGap = 4f;
        private const float SectionGap = 8f;
        private const float ControlRowHeight = 28f;
        private const float NumericFieldWidth = 90f;
        private const float StepButtonWidth = 44f;
        private const float StepButtonGap = 4f;
        private const float PresetButtonWidth = 120f;

        private const string Title = "Produce controls";
        private const string PresetHeading = "Preset";
        private const string PresetNameLabel = "Preset name";
        private const string SavePresetLabel = "Save as preset";
        private const string IndefiniteMode = "Produce indefinitely";
        private const string MaintainMode = "Maintain stock";
        private const string WaitingStatus = "Waiting for stock to fall";
        private const string NotWaitingStatus = "Not waiting for stock to fall";
        private const string WorkersHeading = "Workers";
        private const string MaterialsHeading = "Materials";
        private const string AnyWorkerMode = "Any eligible pawn";
        private const string SelectedWorkerMode = "Selected pawns";
        private const string WorkerSelectionLabel = "Selection";
        private const string MinConstructionSkillLabel = "Minimum Construction skill";
        private const string FixedMaterialMessage =
            "Product is always made of the same material.";
        private const string UnavailableWorkerSuffix = " (unavailable)";
        private const string PresetNameRequiredMessage = "Enter a preset name.";
        private const string PresetSavedMessage = "Preset saved.";
        private const string PresetOverwriteConfirmation =
            "A preset with this name already exists. Overwrite it?";

        private readonly Map map;
        private readonly IntVec3 cell;

        // This is UI memory only. It is deliberately not part of ProduceLoopRecord or the save state.
        private int lastPositiveTarget = 10;
        private string targetBuffer = "0";
        private string resumeBelowBuffer = "0";
        private string minConstructionSkillBuffer = "0";
        private string presetNameBuffer = "";
        private int syncedTargetCount;
        private int syncedEffectiveResumeBelow;
        private int syncedMinConstructionSkill;
        private bool buffersInitialized;
        private Vector2 controlsScroll;

        public Dialog_ProduceControls(Map map, IntVec3 cell)
        {
            this.map = map;
            this.cell = cell;

            ProduceLoopRecord loop = FindLoop();
            if (loop != null && loop.targetCount > 0)
            {
                lastPositiveTarget = loop.targetCount;
            }

            RefreshBuffers(loop);
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

                // Measure the fullest state so switching from indefinite to maintain stock cannot
                // draw newly visible rows into the footer before the next window layout pass.
                float contentHeight = ControlsHeight(contentWidth, true, FindLoop());
                float height = Mathf.Min(fixedHeight + contentHeight,
                    UI.screenHeight * MaxScreenHeightFraction);
                return new Vector2(WindowWidth,
                    Mathf.Max(fixedHeight + Text.LineHeight, height));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            ProduceLoopRecord loop = FindLoop();
            if (loop == null)
            {
                Close();
                return;
            }

            SyncBuffersIfRecordChanged(loop);

            float contentWidth = ContentWidth(inRect.width);
            float bottom = inRect.height - BottomButtonsHeight;
            Rect controlsRect = new Rect(ContentLeft, 0f, contentWidth + ScrollbarGutter,
                Mathf.Max(1f, bottom));
            // The full measured content height keeps the scroll viewport safe if the mode changes
            // during this draw call, while hidden rows remain absent from the window face.
            float contentHeight = ControlsHeight(contentWidth, true, loop);
            if (contentHeight <= controlsRect.height)
            {
                controlsScroll = Vector2.zero;
                GUI.BeginGroup(new Rect(ContentLeft, 0f, contentWidth, controlsRect.height));
                DrawControls(contentWidth);
                GUI.EndGroup();
            }
            else
            {
                Rect controlsView = new Rect(0f, 0f, contentWidth, contentHeight);
                Widgets.BeginScrollView(controlsRect, ref controlsScroll, controlsView);
                DrawControls(contentWidth);
                Widgets.EndScrollView();
            }

            if (Widgets.ButtonText(new Rect(ContentLeft + contentWidth - 120f, bottom, 120f, 36f),
                    "Close"))
            {
                Close();
            }
        }

        private void DrawControls(float width)
        {
            float controlsX = LabelColumnWidth + LabelColumnGap;
            float controlsWidth = width - controlsX;
            float y = 0f;

            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight(Title, width);
            Widgets.Label(new Rect(0f, y, width, titleHeight), Title);
            TooltipHandler.TipRegion(new Rect(0f, y, width, titleHeight),
                "Choose whether this program runs continuously or maintains a stock target.");
            y += titleHeight + SectionGap;
            Text.Font = GameFont.Small;

            float presetHeadingHeight = SectionHeadingHeight(PresetHeading, width);
            DrawSectionHeading(PresetHeading, y, width, presetHeadingHeight);
            y += presetHeadingHeight + RowGap;

            DrawRowLabel(PresetNameLabel, y, ControlRowHeight);
            float presetFieldWidth = controlsWidth - PresetButtonWidth - StepButtonGap;
            presetNameBuffer = Widgets.TextField(
                new Rect(controlsX, y, presetFieldWidth, ControlRowHeight), presetNameBuffer);
            if (Widgets.ButtonText(
                    new Rect(controlsX + presetFieldWidth + StepButtonGap, y,
                        PresetButtonWidth, ControlRowHeight), SavePresetLabel))
            {
                SavePreset();
            }

            y += ControlRowHeight + SectionGap;

            float indefiniteHeight = RadioRowHeight(IndefiniteMode, controlsWidth);
            float maintainHeight = RadioRowHeight(MaintainMode, controlsWidth);
            float modeHeight = indefiniteHeight + RowGap + maintainHeight;
            DrawRowLabel("Mode", y, modeHeight);

            ProduceLoopRecord loop = FindLoop();
            if (loop == null)
            {
                Close();
                return;
            }

            bool maintainStock = loop.targetCount > 0;
            if (Widgets.RadioButtonLabeled(
                    new Rect(controlsX, y, controlsWidth, indefiniteHeight),
                    IndefiniteMode,
                    !maintainStock))
            {
                loop = CommitTarget(0);
                if (loop == null)
                {
                    return;
                }

                maintainStock = false;
            }

            y += indefiniteHeight + RowGap;
            if (Widgets.RadioButtonLabeled(
                    new Rect(controlsX, y, controlsWidth, maintainHeight),
                    MaintainMode,
                    maintainStock))
            {
                int rememberedTarget = lastPositiveTarget > 0 ? lastPositiveTarget : 10;
                loop = CommitTarget(rememberedTarget);
                if (loop == null)
                {
                    return;
                }

                maintainStock = true;
            }

            y += maintainHeight + RowGap;
            if (maintainStock)
            {
                int targetMaximum = TargetMaximum(loop);
                DrawRowLabel("Target", y, ControlRowHeight);
                TooltipHandler.TipRegion(new Rect(0f, y, width, ControlRowHeight),
                    "The program waits once stored stock reaches this target.");

                int typedTarget = loop.targetCount;
                Widgets.TextFieldNumeric(
                    new Rect(controlsX, y, NumericFieldWidth, ControlRowHeight),
                    ref typedTarget,
                    ref targetBuffer,
                    1,
                    targetMaximum);
                if (typedTarget != loop.targetCount)
                {
                    loop = CommitTarget(Mathf.Clamp(typedTarget, 1, targetMaximum));
                    if (loop == null)
                    {
                        return;
                    }

                    targetMaximum = TargetMaximum(loop);
                }

                int targetStep = 0;
                float stepX = controlsX + NumericFieldWidth + StepButtonGap;
                if (Widgets.ButtonText(
                        new Rect(stepX, y, StepButtonWidth, ControlRowHeight), "-10"))
                {
                    targetStep = -10;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + StepButtonWidth + StepButtonGap, y,
                            StepButtonWidth, ControlRowHeight), "-1"))
                {
                    targetStep = -1;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + (StepButtonWidth + StepButtonGap) * 2f, y,
                            StepButtonWidth, ControlRowHeight), "+1"))
                {
                    targetStep = 1;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + (StepButtonWidth + StepButtonGap) * 3f, y,
                            StepButtonWidth, ControlRowHeight), "+10"))
                {
                    targetStep = 10;
                }

                if (targetStep != 0)
                {
                    loop = CommitTarget(StepValue(loop.targetCount, targetStep, 1, targetMaximum));
                    if (loop == null)
                    {
                        return;
                    }
                }

                y += ControlRowHeight + RowGap;

                int resumeMaximum = Mathf.Max(0, loop.targetCount - 1);
                int typedResumeBelow = loop.EffectiveResumeBelow;
                DrawRowLabel("Resume below", y, ControlRowHeight);
                TooltipHandler.TipRegion(new Rect(0f, y, width, ControlRowHeight),
                    "When waiting, the program resumes at or below this stored-stock amount.");
                Widgets.TextFieldNumeric(
                    new Rect(controlsX, y, NumericFieldWidth, ControlRowHeight),
                    ref typedResumeBelow,
                    ref resumeBelowBuffer,
                    0,
                    resumeMaximum);
                if (typedResumeBelow != loop.EffectiveResumeBelow)
                {
                    loop = CommitResumeBelow(Mathf.Clamp(typedResumeBelow, 0, resumeMaximum));
                    if (loop == null)
                    {
                        return;
                    }
                }

                int resumeStep = 0;
                stepX = controlsX + NumericFieldWidth + StepButtonGap;
                if (Widgets.ButtonText(
                        new Rect(stepX, y, StepButtonWidth, ControlRowHeight), "-10"))
                {
                    resumeStep = -10;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + StepButtonWidth + StepButtonGap, y,
                            StepButtonWidth, ControlRowHeight), "-1"))
                {
                    resumeStep = -1;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + (StepButtonWidth + StepButtonGap) * 2f, y,
                            StepButtonWidth, ControlRowHeight), "+1"))
                {
                    resumeStep = 1;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + (StepButtonWidth + StepButtonGap) * 3f, y,
                            StepButtonWidth, ControlRowHeight), "+10"))
                {
                    resumeStep = 10;
                }

                if (resumeStep != 0)
                {
                    loop = CommitResumeBelow(
                        StepValue(loop.EffectiveResumeBelow, resumeStep, 0, resumeMaximum));
                    if (loop == null)
                    {
                        return;
                    }
                }

                y += ControlRowHeight + RowGap;

                string status = loop.waitingForResume ? WaitingStatus : NotWaitingStatus;
                float statusTextHeight = Text.CalcHeight(status, controlsWidth);
                float statusRowHeight = Mathf.Max(ControlRowHeight, statusTextHeight);
                DrawRowLabel("Status", y, statusRowHeight);
                Rect statusRect = new Rect(controlsX,
                    y + (statusRowHeight - statusTextHeight) / 2f,
                    controlsWidth,
                    statusTextHeight);
                Widgets.Label(statusRect, status);
                TooltipHandler.TipRegion(new Rect(0f, y, width, statusRowHeight),
                    "This status reflects whether the production program is latched waiting for stock to fall.");
                y += statusRowHeight;
            }

            y += SectionGap;

            float workersHeadingHeight = SectionHeadingHeight(WorkersHeading, width);
            DrawSectionHeading(WorkersHeading, y, width, workersHeadingHeight);
            y += workersHeadingHeight + RowGap;

            float anyWorkerHeight = RadioRowHeight(AnyWorkerMode, controlsWidth);
            float selectedWorkerHeight = RadioRowHeight(SelectedWorkerMode, controlsWidth);
            float workerModeHeight = anyWorkerHeight + RowGap + selectedWorkerHeight;
            DrawRowLabel(WorkerSelectionLabel, y, workerModeHeight);

            if (Widgets.RadioButtonLabeled(
                    new Rect(controlsX, y, controlsWidth, anyWorkerHeight),
                    AnyWorkerMode,
                    !loop.restrictToSelectedWorkers))
            {
                loop = CommitWorkerRestriction(false);
                if (loop == null)
                {
                    return;
                }
            }

            y += anyWorkerHeight + RowGap;
            if (Widgets.RadioButtonLabeled(
                    new Rect(controlsX, y, controlsWidth, selectedWorkerHeight),
                    SelectedWorkerMode,
                    loop.restrictToSelectedWorkers))
            {
                loop = CommitWorkerRestriction(true);
                if (loop == null)
                {
                    return;
                }
            }

            y += selectedWorkerHeight + RowGap;
            if (loop.restrictToSelectedWorkers)
            {
                List<Pawn> availableWorkers = AvailableWorkerCandidates();
                List<Pawn> workerRows = WorkerCandidates(loop, availableWorkers);
                for (int i = 0; i < workerRows.Count; i++)
                {
                    Pawn pawn = workerRows[i];
                    string workerLabel = WorkerLabel(pawn, !availableWorkers.Contains(pawn));
                    float workerRowHeight = CheckboxRowHeight(workerLabel, controlsWidth);
                    bool selected = loop.allowedWorkers != null && loop.allowedWorkers.Contains(pawn);
                    bool wasSelected = selected;
                    Widgets.CheckboxLabeled(
                        new Rect(controlsX, y, controlsWidth, workerRowHeight),
                        workerLabel,
                        ref selected);
                    if (selected != wasSelected)
                    {
                        List<Pawn> allowedWorkers = CopyAllowedWorkers(loop);
                        if (selected)
                        {
                            if (!allowedWorkers.Contains(pawn))
                            {
                                allowedWorkers.Add(pawn);
                            }
                        }
                        else
                        {
                            allowedWorkers.RemoveAll(worker => worker == pawn);
                        }

                        loop = CommitAllowedWorkers(allowedWorkers);
                        if (loop == null)
                        {
                            return;
                        }
                    }

                    y += workerRowHeight + RowGap;
                }
            }

            DrawRowLabel(MinConstructionSkillLabel, y, ControlRowHeight);
            TooltipHandler.TipRegion(new Rect(0f, y, width, ControlRowHeight),
                "This floor applies only when vanilla checks Construction for frame finishing or Construction-work-type delivery; it does not apply to hauling materials.");

            int typedMinConstructionSkill = loop.minConstructionSkill;
            Widgets.TextFieldNumeric(
                new Rect(controlsX, y, NumericFieldWidth, ControlRowHeight),
                ref typedMinConstructionSkill,
                ref minConstructionSkillBuffer,
                0,
                20);
            if (typedMinConstructionSkill != loop.minConstructionSkill)
            {
                loop = CommitMinConstructionSkill(Mathf.Clamp(typedMinConstructionSkill, 0, 20));
                if (loop == null)
                {
                    return;
                }
            }

            int skillStep = 0;
            float skillStepX = controlsX + NumericFieldWidth + StepButtonGap;
            if (Widgets.ButtonText(
                    new Rect(skillStepX, y, StepButtonWidth, ControlRowHeight), "-1"))
            {
                skillStep = -1;
            }

            if (Widgets.ButtonText(
                    new Rect(skillStepX + StepButtonWidth + StepButtonGap, y,
                        StepButtonWidth, ControlRowHeight), "+1"))
            {
                skillStep = 1;
            }

            if (skillStep != 0)
            {
                loop = CommitMinConstructionSkill(
                    StepValue(loop.minConstructionSkill, skillStep, 0, 20));
                if (loop == null)
                {
                    return;
                }
            }

            y += ControlRowHeight + RowGap;
            y += SectionGap;

            float materialsHeadingHeight = SectionHeadingHeight(MaterialsHeading, width);
            DrawSectionHeading(MaterialsHeading, y, width, materialsHeadingHeight);
            y += materialsHeadingHeight + RowGap;

            if (loop.thingDef == null || !loop.thingDef.MadeFromStuff)
            {
                float fixedMaterialHeight = ValueRowHeight(FixedMaterialMessage, controlsWidth);
                DrawRowLabel("Allowed materials", y, fixedMaterialHeight);
                Widgets.Label(
                    new Rect(controlsX, y, controlsWidth, fixedMaterialHeight),
                    FixedMaterialMessage);
                y += fixedMaterialHeight;
            }
            else
            {
                List<ThingDef> stuffOptions = AllowedStuffs(loop);
                for (int i = 0; i < stuffOptions.Count; i++)
                {
                    ThingDef stuff = stuffOptions[i];
                    string stuffLabel = stuff.LabelCap.ToString();
                    float stuffRowHeight = CheckboxRowHeight(stuffLabel, controlsWidth);
                    bool allowed = loop.allowedStuff != null && loop.allowedStuff.Contains(stuff);
                    bool wasAllowed = allowed;
                    Widgets.CheckboxLabeled(
                        new Rect(controlsX, y, controlsWidth, stuffRowHeight),
                        stuffLabel,
                        ref allowed);
                    if (allowed != wasAllowed)
                    {
                        if (!allowed && IsLastAllowedStuff(loop, stuffOptions))
                        {
                            // Do not let the UI empty a stuffable program's allowed set: the next
                            // resolution would return null and silently stop production forever.
                            allowed = true;
                        }
                        else
                        {
                            List<ThingDef> allowedStuff = CopyAllowedStuff(loop);
                            if (allowed)
                            {
                                if (!allowedStuff.Contains(stuff))
                                {
                                    allowedStuff.Add(stuff);
                                }
                            }
                            else
                            {
                                allowedStuff.RemoveAll(candidate => candidate == stuff);
                            }

                            loop = CommitAllowedStuff(allowedStuff);
                            if (loop == null)
                            {
                                return;
                            }
                        }
                    }

                    y += stuffRowHeight;
                    if (i < stuffOptions.Count - 1)
                    {
                        y += RowGap;
                    }
                }
            }
        }

        // Resolve the loop when the button is clicked so a delayed overwrite confirmation never
        // captures a record that has already been removed from the map.
        private void SavePreset()
        {
            ProduceLoopRecord loop = FindLoop();
            if (loop == null)
            {
                return;
            }

            string trimmedName = presetNameBuffer.Trim();
            if (trimmedName.Length == 0)
            {
                Messages.Message(
                    PresetNameRequiredMessage,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            if (component == null)
            {
                return;
            }

            ProduceControlPreset existing = component.FindPresetByName(trimmedName);
            if (existing == null)
            {
                string reason;
                ProduceControlPreset created = component.CreatePresetFromLoop(
                    trimmedName,
                    loop,
                    out reason);
                ReportPresetSaveResult(created != null, reason);
                return;
            }

            int existingPresetId = existing.id;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                PresetOverwriteConfirmation,
                () =>
                {
                    ProduceLoopRecord currentLoop = FindLoop();
                    if (currentLoop == null)
                    {
                        return;
                    }

                    ProduceLoopMapComponent currentComponent = ProduceLoopMapComponent.For(map);
                    if (currentComponent == null)
                    {
                        return;
                    }

                    string reason;
                    bool overwritten = currentComponent.TryOverwritePresetFromLoop(
                        existingPresetId,
                        currentLoop,
                        out reason);
                    ReportPresetSaveResult(overwritten, reason);
                },
                destructive: true));
        }

        private void ReportPresetSaveResult(bool saved, string reason)
        {
            if (saved)
            {
                Messages.Message(
                    PresetSavedMessage,
                    MessageTypeDefOf.TaskCompletion,
                    historical: false);
                presetNameBuffer = "";
                return;
            }

            Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
        }

        private static void DrawRowLabel(string label, float rowY, float controlHeight)
        {
            float labelHeight = Text.CalcHeight(label, LabelColumnWidth);
            float labelY = rowY + (controlHeight - labelHeight) / 2f;
            Widgets.Label(new Rect(0f, labelY, LabelColumnWidth, labelHeight), label);
        }

        private static void DrawSectionHeading(
            string heading,
            float rowY,
            float width,
            float headingHeight)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, rowY, width, headingHeight), heading);
            Text.Font = GameFont.Small;
        }

        private static float SectionHeadingHeight(string heading, float width)
        {
            Text.Font = GameFont.Medium;
            float headingHeight = Mathf.Max(ControlRowHeight, Text.CalcHeight(heading, width));
            Text.Font = GameFont.Small;
            return headingHeight;
        }

        private static float CheckboxRowHeight(string label, float width)
        {
            return Mathf.Max(ControlRowHeight,
                Text.CalcHeight(label, Mathf.Max(1f, width - 24f)));
        }

        private static float ValueRowHeight(string label, float width)
        {
            return Mathf.Max(ControlRowHeight, Text.CalcHeight(label, width));
        }

        private static float RadioRowHeight(string label, float width)
        {
            return Mathf.Max(ControlRowHeight,
                Text.CalcHeight(label, Mathf.Max(1f, width - 28f)));
        }

        private static float StatusRowHeight(float width)
        {
            return Mathf.Max(
                ControlRowHeight,
                Text.CalcHeight(WaitingStatus, width),
                Text.CalcHeight(NotWaitingStatus, width));
        }

        private static float ContentWidth(float availableWidth)
        {
            return availableWidth - ContentLeft * 2f;
        }

        private float ControlsHeight(float width, bool maintainStock, ProduceLoopRecord loop)
        {
            float controlsWidth = width - LabelColumnWidth - LabelColumnGap;
            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight(Title, width);
            Text.Font = GameFont.Small;

            float height = titleHeight + SectionGap;
            height += SectionHeadingHeight(PresetHeading, width) + RowGap;
            height += ControlRowHeight + SectionGap;
            height += RadioRowHeight(IndefiniteMode, controlsWidth) + RowGap;
            height += RadioRowHeight(MaintainMode, controlsWidth) + RowGap;
            if (maintainStock)
            {
                height += ControlRowHeight + RowGap;
                height += ControlRowHeight + RowGap;
                height += StatusRowHeight(controlsWidth);
            }

            height += SectionGap;
            height += SectionHeadingHeight(WorkersHeading, width) + RowGap;

            height += RadioRowHeight(AnyWorkerMode, controlsWidth) + RowGap;
            height += RadioRowHeight(SelectedWorkerMode, controlsWidth) + RowGap;

            List<Pawn> availableWorkers = AvailableWorkerCandidates();
            List<Pawn> workerRows = WorkerCandidates(loop, availableWorkers);
            for (int i = 0; i < workerRows.Count; i++)
            {
                height += CheckboxRowHeight(
                    WorkerLabel(workerRows[i], !availableWorkers.Contains(workerRows[i])),
                    controlsWidth) + RowGap;
            }

            height += ControlRowHeight + RowGap;
            height += SectionGap;
            height += SectionHeadingHeight(MaterialsHeading, width) + RowGap;

            if (loop == null || loop.thingDef == null || !loop.thingDef.MadeFromStuff)
            {
                height += ValueRowHeight(FixedMaterialMessage, controlsWidth);
            }
            else
            {
                List<ThingDef> stuffOptions = AllowedStuffs(loop);
                for (int i = 0; i < stuffOptions.Count; i++)
                {
                    height += CheckboxRowHeight(stuffOptions[i].LabelCap.ToString(), controlsWidth);
                    if (i < stuffOptions.Count - 1)
                    {
                        height += RowGap;
                    }
                }
            }

            return height;
        }

        private List<Pawn> AvailableWorkerCandidates()
        {
            List<Pawn> candidates = new List<Pawn>();
            if (map != null && map.mapPawns != null)
            {
                List<Pawn> freeColonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < freeColonists.Count; i++)
                {
                    AddWorkerIfMissing(candidates, freeColonists[i]);
                }
            }

            List<EmploymentContract> employments = IntercolonyWorldComponent.Current?.Employments;
            if (employments != null)
            {
                for (int i = 0; i < employments.Count; i++)
                {
                    EmploymentContract contract = employments[i];
                    Pawn employee = contract?.pawn;
                    if (contract != null && contract.status == EmploymentStatus.Active &&
                        employee != null && employee.Spawned && employee.Map == map)
                    {
                        AddWorkerIfMissing(candidates, employee);
                    }
                }
            }

            return candidates;
        }

        private static List<Pawn> WorkerCandidates(
            ProduceLoopRecord loop,
            List<Pawn> availableWorkers)
        {
            List<Pawn> candidates = new List<Pawn>(availableWorkers);
            if (loop?.allowedWorkers != null)
            {
                for (int i = 0; i < loop.allowedWorkers.Count; i++)
                {
                    AddWorkerIfMissing(candidates, loop.allowedWorkers[i]);
                }
            }

            candidates.Sort((left, right) =>
            {
                return string.CompareOrdinal(WorkerSortLabel(left), WorkerSortLabel(right));
            });
            return candidates;
        }

        private static void AddWorkerIfMissing(List<Pawn> workers, Pawn worker)
        {
            if (worker != null && !workers.Contains(worker))
            {
                workers.Add(worker);
            }
        }

        private static string WorkerSortLabel(Pawn pawn)
        {
            return pawn?.LabelShortCap ?? "";
        }

        private static string WorkerLabel(Pawn pawn, bool unavailable)
        {
            string label = (pawn?.LabelShortCap ?? "Unknown pawn") +
                            " (Construction: " + ConstructionLevel(pawn) + ")";
            return unavailable ? label + UnavailableWorkerSuffix : label;
        }

        private static int ConstructionLevel(Pawn pawn)
        {
            if (pawn == null)
            {
                return 0;
            }

            if (pawn.skills != null)
            {
                return pawn.skills.GetSkill(SkillDefOf.Construction).Level;
            }

            return pawn.IsColonyMech ? pawn.RaceProps.mechFixedSkillLevel : 0;
        }

        private static List<ThingDef> AllowedStuffs(ProduceLoopRecord loop)
        {
            List<ThingDef> stuffs = new List<ThingDef>();
            if (loop == null || loop.thingDef == null || !loop.thingDef.MadeFromStuff)
            {
                return stuffs;
            }

            foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(loop.thingDef))
            {
                if (stuff != null && !stuffs.Contains(stuff))
                {
                    stuffs.Add(stuff);
                }
            }

            stuffs.Sort((left, right) =>
            {
                int labelComparison = string.CompareOrdinal(
                    left.LabelCap.ToString(), right.LabelCap.ToString());
                return labelComparison != 0
                    ? labelComparison
                    : string.CompareOrdinal(left.defName, right.defName);
            });
            return stuffs;
        }

        private static bool IsLastAllowedStuff(
            ProduceLoopRecord loop,
            List<ThingDef> stuffOptions)
        {
            if (loop == null || loop.allowedStuff == null)
            {
                return false;
            }

            int allowedCount = 0;
            for (int i = 0; i < stuffOptions.Count; i++)
            {
                if (loop.allowedStuff.Contains(stuffOptions[i]))
                {
                    allowedCount++;
                }
            }

            return allowedCount == 1;
        }

        private static List<Pawn> CopyAllowedWorkers(ProduceLoopRecord loop)
        {
            return loop?.allowedWorkers == null
                ? new List<Pawn>()
                : new List<Pawn>(loop.allowedWorkers);
        }

        private static List<ThingDef> CopyAllowedStuff(ProduceLoopRecord loop)
        {
            return loop?.allowedStuff == null
                ? new List<ThingDef>()
                : new List<ThingDef>(loop.allowedStuff);
        }

        private ProduceLoopRecord FindLoop()
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            return component == null ? null : component.Find(cell);
        }

        private void SyncBuffersIfRecordChanged(ProduceLoopRecord loop)
        {
            int effectiveResumeBelow = loop.EffectiveResumeBelow;
            if (!buffersInitialized || syncedTargetCount != loop.targetCount ||
                syncedEffectiveResumeBelow != effectiveResumeBelow ||
                syncedMinConstructionSkill != loop.minConstructionSkill)
            {
                RefreshBuffers(loop);
            }
        }

        private void RefreshBuffers(ProduceLoopRecord loop)
        {
            if (loop == null)
            {
                return;
            }

            targetBuffer = loop.targetCount.ToString();
            resumeBelowBuffer = loop.EffectiveResumeBelow.ToString();
            minConstructionSkillBuffer = loop.minConstructionSkill.ToString();
            syncedTargetCount = loop.targetCount;
            syncedEffectiveResumeBelow = loop.EffectiveResumeBelow;
            syncedMinConstructionSkill = loop.minConstructionSkill;
            buffersInitialized = true;
            if (loop.targetCount > 0)
            {
                lastPositiveTarget = loop.targetCount;
            }
        }

        private ProduceLoopRecord CommitTarget(int value)
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            if (component == null)
            {
                Close();
                return null;
            }

            component.SetTargetCount(cell, value);
            ProduceLoopRecord loop = FindLoop();
            if (loop == null)
            {
                Close();
                return null;
            }

            // SetTargetCount may clamp Resume below as a consequence of this target change.
            RefreshBuffers(loop);
            return loop;
        }

        private ProduceLoopRecord CommitResumeBelow(int value)
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            if (component == null)
            {
                Close();
                return null;
            }

            component.SetResumeBelow(cell, value);
            ProduceLoopRecord loop = FindLoop();
            if (loop == null)
            {
                Close();
                return null;
            }

            RefreshBuffers(loop);
            return loop;
        }

        private ProduceLoopRecord CommitWorkerRestriction(bool restrict)
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            if (component == null)
            {
                Close();
                return null;
            }

            component.SetWorkerRestriction(cell, restrict);
            ProduceLoopRecord loop = FindLoop();
            if (loop == null)
            {
                Close();
                return null;
            }

            return loop;
        }

        private ProduceLoopRecord CommitAllowedWorkers(List<Pawn> workers)
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            if (component == null)
            {
                Close();
                return null;
            }

            component.SetAllowedWorkers(cell, workers);
            ProduceLoopRecord loop = FindLoop();
            if (loop == null)
            {
                Close();
                return null;
            }

            return loop;
        }

        private ProduceLoopRecord CommitMinConstructionSkill(int level)
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            if (component == null)
            {
                Close();
                return null;
            }

            component.SetMinConstructionSkill(cell, level);
            ProduceLoopRecord loop = FindLoop();
            if (loop == null)
            {
                Close();
                return null;
            }

            RefreshBuffers(loop);
            return loop;
        }

        private ProduceLoopRecord CommitAllowedStuff(List<ThingDef> stuffs)
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            if (component == null)
            {
                Close();
                return null;
            }

            component.SetAllowedStuff(cell, stuffs);
            ProduceLoopRecord loop = FindLoop();
            if (loop == null)
            {
                Close();
                return null;
            }

            return loop;
        }

        private static int TargetMaximum(ProduceLoopRecord loop)
        {
            return Math.Max(IntercolonyMod.Settings.maxProduceTarget, loop.targetCount);
        }

        private static int StepValue(int current, int delta, int minimum, int maximum)
        {
            long stepped = (long)current + delta;
            if (stepped < minimum)
            {
                return minimum;
            }

            if (stepped > maximum)
            {
                return maximum;
            }

            return (int)stepped;
        }
    }
}
