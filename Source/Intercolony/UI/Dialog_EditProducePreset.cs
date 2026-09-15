using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Edits the reusable settings of one saved production preset without retaining the preset
    /// object itself, so removal or replacement of that preset while this window is open is safe.
    /// </summary>
    public class Dialog_EditProducePreset : Window
    {
        private const float WindowWidth = 640f;
        private const float WindowMargin = 18f;

        /// <summary>
        /// The measured content is capped so the footer stays on-screen while all rows remain
        /// reachable through the scroll view.
        /// </summary>
        private const float MaxScreenHeightFraction = 0.95f;
        private const float BottomButtonsHeight = 40f;
        private const float ContentInset = 8f;
        private const float ScrollbarGutter = 16f;

        /// <summary>
        /// Labels use a fixed column so the controls in every section share one aligned start.
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
        private const float FooterButtonWidth = 120f;
        private const float FooterButtonGap = 8f;

        private const int DefaultLastPositiveTarget = 10;
        private const int MinimumTarget = 1;
        private const int MinimumResumeBelow = 0;
        private const int MinimumConstructionSkill = 0;
        private const int MaximumConstructionSkill = 20;

        private const string IndefiniteMode = "Produce indefinitely";
        private const string MaintainMode = "Maintain stock";
        private const string ModeLabel = "Mode";
        private const string TargetLabel = "Target";
        private const string ResumeBelowLabel = "Resume below";
        private const string WorkersHeading = "Workers";
        private const string WorkerSelectionLabel = "Selection";
        private const string AnyWorkerMode = "Any eligible pawn";
        private const string SelectedWorkerMode = "Selected pawns";
        private const string MinConstructionSkillLabel = "Minimum Construction skill";
        private const string MaterialsHeading = "Materials";
        private const string AllowedMaterialsLabel = "Allowed materials";
        private const string AddMaterialLabel = "Add material";
        private const string NoMaterialRestrictionMessage =
            "No material restriction. This preset keeps each object's own materials.";
        private const string UnnamedPresetLabel = "Unnamed preset";
        private const string UnknownPawnLabel = "Unknown pawn";
        private const string ConstructionLabelPrefix = " (Construction: ";
        private const string ConstructionLabelSuffix = ")";
        private const string UnavailableWorkerSuffix = " (unavailable)";
        private const string MinusTenStepLabel = "-10";
        private const string MinusOneStepLabel = "-1";
        private const string PlusOneStepLabel = "+1";
        private const string PlusTenStepLabel = "+10";
        private const string AcceptLabel = "Accept";
        private const string CancelLabel = "Cancel";
        private const string TitleTooltip =
            "Edit this saved preset's settings. Changes are kept until you accept.";
        private const string TargetTooltip =
            "The stored-stock target this preset will maintain.";
        private const string ResumeBelowTooltip =
            "When waiting, the preset resumes at or below this stored-stock amount.";
        private const string MinConstructionSkillTooltip =
            "This floor applies only when vanilla checks Construction for frame finishing or Construction-work-type delivery; it does not apply to hauling materials.";

        private readonly Map map;
        private readonly int presetId;

        // This is UI memory only. It is deliberately not part of ProduceControlPreset or the save state.
        private int lastPositiveTarget = DefaultLastPositiveTarget;
        private string targetBuffer = "0";
        private string resumeBelowBuffer = "0";
        private string minConstructionSkillBuffer = "0";
        private Vector2 controlsScroll;

        private readonly ProduceControlPreset workingCopy;

        public Dialog_EditProducePreset(Map map, int presetId)
        {
            this.map = map;
            this.presetId = presetId;

            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            workingCopy = CopyPresetSettings(preset);
            if (workingCopy != null)
            {
                if (workingCopy.targetCount > 0)
                {
                    lastPositiveTarget = workingCopy.targetCount;
                }

                RefreshBuffers();
            }

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
                ProduceControlPreset preset = FindPreset();

                // Measure the fullest mode so switching to maintain stock cannot expose rows under the footer.
                float contentHeight = ControlsHeight(contentWidth, true, preset);
                float height = Mathf.Min(fixedHeight + contentHeight,
                    UI.screenHeight * MaxScreenHeightFraction);
                return new Vector2(WindowWidth,
                    Mathf.Max(fixedHeight + Text.LineHeight, height));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            if (map == null || component == null || preset == null || workingCopy == null)
            {
                Close();
                return;
            }

            float contentWidth = ContentWidth(inRect.width);
            float bottom = inRect.height - BottomButtonsHeight;
            Rect controlsRect = new Rect(ContentLeft, 0f, contentWidth + ScrollbarGutter,
                Mathf.Max(1f, bottom));
            // The full measured content keeps the viewport safe when a mode or worker selection changes during draw.
            float contentHeight = ControlsHeight(contentWidth, true, preset);
            if (contentHeight <= controlsRect.height)
            {
                controlsScroll = Vector2.zero;
                GUI.BeginGroup(new Rect(ContentLeft, 0f, contentWidth, controlsRect.height));
                DrawControls(contentWidth, preset);
                GUI.EndGroup();
            }
            else
            {
                Rect controlsView = new Rect(0f, 0f, contentWidth, contentHeight);
                Widgets.BeginScrollView(controlsRect, ref controlsScroll, controlsView);
                DrawControls(contentWidth, preset);
                Widgets.EndScrollView();
            }

            float footerX = ContentLeft + contentWidth - FooterButtonWidth * 2f - FooterButtonGap;
            if (Widgets.ButtonText(
                    new Rect(footerX, bottom, FooterButtonWidth, 36f), AcceptLabel))
            {
                AcceptChanges();
            }

            if (Widgets.ButtonText(
                    new Rect(footerX + FooterButtonWidth + FooterButtonGap, bottom,
                        FooterButtonWidth, 36f), CancelLabel))
            {
                CancelChanges();
            }
        }

        public override void OnAcceptKeyPressed()
        {
            AcceptChanges();
            if (Event.current != null)
            {
                Event.current.Use();
            }
        }

        public override void OnCancelKeyPressed()
        {
            CancelChanges();
            if (Event.current != null)
            {
                Event.current.Use();
            }
        }

        private void DrawControls(float width, ProduceControlPreset preset)
        {
            if (preset == null || workingCopy == null)
            {
                Close();
                return;
            }

            float controlsX = LabelColumnWidth + LabelColumnGap;
            float controlsWidth = width - controlsX;
            float y = 0f;

            string title = PresetTitle(preset);
            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight(title, width);
            Widgets.Label(new Rect(0f, y, width, titleHeight), title);
            TooltipHandler.TipRegion(new Rect(0f, y, width, titleHeight), TitleTooltip);
            y += titleHeight + SectionGap;
            Text.Font = GameFont.Small;

            float indefiniteHeight = RadioRowHeight(IndefiniteMode, controlsWidth);
            float maintainHeight = RadioRowHeight(MaintainMode, controlsWidth);
            float modeHeight = indefiniteHeight + RowGap + maintainHeight;
            DrawRowLabel(ModeLabel, y, modeHeight);

            bool maintainStock = workingCopy.targetCount > 0;
            if (Widgets.RadioButtonLabeled(
                    new Rect(controlsX, y, controlsWidth, indefiniteHeight),
                    IndefiniteMode,
                    !maintainStock))
            {
                SetTargetCount(0);
                maintainStock = false;
            }

            y += indefiniteHeight + RowGap;
            if (Widgets.RadioButtonLabeled(
                    new Rect(controlsX, y, controlsWidth, maintainHeight),
                    MaintainMode,
                    maintainStock))
            {
                int rememberedTarget = lastPositiveTarget > 0
                    ? lastPositiveTarget
                    : DefaultLastPositiveTarget;
                SetTargetCount(rememberedTarget);
                maintainStock = true;
            }

            y += maintainHeight + RowGap;
            if (maintainStock)
            {
                int targetMaximum = TargetMaximum();
                DrawRowLabel(TargetLabel, y, ControlRowHeight);
                TooltipHandler.TipRegion(new Rect(0f, y, width, ControlRowHeight),
                    TargetTooltip);

                int typedTarget = workingCopy.targetCount;
                Widgets.TextFieldNumeric(
                    new Rect(controlsX, y, NumericFieldWidth, ControlRowHeight),
                    ref typedTarget,
                    ref targetBuffer,
                    MinimumTarget,
                    targetMaximum);
                if (typedTarget != workingCopy.targetCount)
                {
                    SetTargetCount(Mathf.Clamp(typedTarget, MinimumTarget, targetMaximum));
                    targetMaximum = TargetMaximum();
                }

                int targetStep = 0;
                float stepX = controlsX + NumericFieldWidth + StepButtonGap;
                if (Widgets.ButtonText(
                        new Rect(stepX, y, StepButtonWidth, ControlRowHeight), MinusTenStepLabel))
                {
                    targetStep = -10;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + StepButtonWidth + StepButtonGap, y,
                            StepButtonWidth, ControlRowHeight), MinusOneStepLabel))
                {
                    targetStep = -1;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + (StepButtonWidth + StepButtonGap) * 2f, y,
                            StepButtonWidth, ControlRowHeight), PlusOneStepLabel))
                {
                    targetStep = 1;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + (StepButtonWidth + StepButtonGap) * 3f, y,
                            StepButtonWidth, ControlRowHeight), PlusTenStepLabel))
                {
                    targetStep = 10;
                }

                if (targetStep != 0)
                {
                    SetTargetCount(StepValue(
                        workingCopy.targetCount, targetStep, MinimumTarget, targetMaximum));
                }

                y += ControlRowHeight + RowGap;

                int resumeMaximum = Mathf.Max(MinimumResumeBelow, workingCopy.targetCount - 1);
                int typedResumeBelow = workingCopy.EffectiveResumeBelow;
                DrawRowLabel(ResumeBelowLabel, y, ControlRowHeight);
                TooltipHandler.TipRegion(new Rect(0f, y, width, ControlRowHeight),
                    ResumeBelowTooltip);
                Widgets.TextFieldNumeric(
                    new Rect(controlsX, y, NumericFieldWidth, ControlRowHeight),
                    ref typedResumeBelow,
                    ref resumeBelowBuffer,
                    MinimumResumeBelow,
                    resumeMaximum);
                if (typedResumeBelow != workingCopy.EffectiveResumeBelow)
                {
                    workingCopy.resumeBelow = Mathf.Clamp(
                        typedResumeBelow, MinimumResumeBelow, resumeMaximum);
                    RefreshBuffers();
                }

                int resumeStep = 0;
                stepX = controlsX + NumericFieldWidth + StepButtonGap;
                if (Widgets.ButtonText(
                        new Rect(stepX, y, StepButtonWidth, ControlRowHeight), MinusTenStepLabel))
                {
                    resumeStep = -10;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + StepButtonWidth + StepButtonGap, y,
                            StepButtonWidth, ControlRowHeight), MinusOneStepLabel))
                {
                    resumeStep = -1;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + (StepButtonWidth + StepButtonGap) * 2f, y,
                            StepButtonWidth, ControlRowHeight), PlusOneStepLabel))
                {
                    resumeStep = 1;
                }

                if (Widgets.ButtonText(
                        new Rect(stepX + (StepButtonWidth + StepButtonGap) * 3f, y,
                            StepButtonWidth, ControlRowHeight), PlusTenStepLabel))
                {
                    resumeStep = 10;
                }

                if (resumeStep != 0)
                {
                    workingCopy.resumeBelow = StepValue(
                        workingCopy.EffectiveResumeBelow,
                        resumeStep,
                        MinimumResumeBelow,
                        resumeMaximum);
                    RefreshBuffers();
                }

                y += ControlRowHeight + RowGap;
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
                    !workingCopy.restrictToSelectedWorkers))
            {
                workingCopy.restrictToSelectedWorkers = false;
            }

            y += anyWorkerHeight + RowGap;
            if (Widgets.RadioButtonLabeled(
                    new Rect(controlsX, y, controlsWidth, selectedWorkerHeight),
                    SelectedWorkerMode,
                    workingCopy.restrictToSelectedWorkers))
            {
                workingCopy.restrictToSelectedWorkers = true;
            }

            y += selectedWorkerHeight + RowGap;
            if (workingCopy.restrictToSelectedWorkers)
            {
                List<Pawn> availableWorkers = AvailableWorkerCandidates();
                List<Pawn> workerRows = WorkerCandidates(workingCopy, availableWorkers);
                for (int i = 0; i < workerRows.Count; i++)
                {
                    Pawn pawn = workerRows[i];
                    string workerLabel = WorkerLabel(pawn, !availableWorkers.Contains(pawn));
                    float workerRowHeight = CheckboxRowHeight(workerLabel, controlsWidth);
                    bool selected = workingCopy.allowedWorkers != null &&
                        workingCopy.allowedWorkers.Contains(pawn);
                    bool wasSelected = selected;
                    Widgets.CheckboxLabeled(
                        new Rect(controlsX, y, controlsWidth, workerRowHeight),
                        workerLabel,
                        ref selected);
                    if (selected != wasSelected)
                    {
                        List<Pawn> allowedWorkers = CopyAllowedWorkers(workingCopy.allowedWorkers);
                        if (selected)
                        {
                            AddWorkerIfMissing(allowedWorkers, pawn);
                        }
                        else
                        {
                            allowedWorkers.RemoveAll(worker => worker == pawn);
                        }

                        workingCopy.allowedWorkers = allowedWorkers;
                    }

                    y += workerRowHeight + RowGap;
                }
            }

            DrawRowLabel(MinConstructionSkillLabel, y, ControlRowHeight);
            TooltipHandler.TipRegion(new Rect(0f, y, width, ControlRowHeight),
                MinConstructionSkillTooltip);

            int typedMinConstructionSkill = workingCopy.minConstructionSkill;
            Widgets.TextFieldNumeric(
                new Rect(controlsX, y, NumericFieldWidth, ControlRowHeight),
                ref typedMinConstructionSkill,
                ref minConstructionSkillBuffer,
                MinimumConstructionSkill,
                MaximumConstructionSkill);
            if (typedMinConstructionSkill != workingCopy.minConstructionSkill)
            {
                workingCopy.minConstructionSkill = Mathf.Clamp(
                    typedMinConstructionSkill,
                    MinimumConstructionSkill,
                    MaximumConstructionSkill);
                RefreshBuffers();
            }

            int skillStep = 0;
            float skillStepX = controlsX + NumericFieldWidth + StepButtonGap;
            if (Widgets.ButtonText(
                    new Rect(skillStepX, y, StepButtonWidth, ControlRowHeight), MinusOneStepLabel))
            {
                skillStep = -1;
            }

            if (Widgets.ButtonText(
                    new Rect(skillStepX + StepButtonWidth + StepButtonGap, y,
                        StepButtonWidth, ControlRowHeight), PlusOneStepLabel))
            {
                skillStep = 1;
            }

            if (skillStep != 0)
            {
                workingCopy.minConstructionSkill = StepValue(
                    workingCopy.minConstructionSkill,
                    skillStep,
                    MinimumConstructionSkill,
                    MaximumConstructionSkill);
                RefreshBuffers();
            }

            y += ControlRowHeight + RowGap;
            y += SectionGap;

            float materialsHeadingHeight = SectionHeadingHeight(MaterialsHeading, width);
            DrawSectionHeading(MaterialsHeading, y, width, materialsHeadingHeight);
            y += materialsHeadingHeight + RowGap;

            List<ThingDef> materialRows = AllowedMaterialRows();
            if (materialRows.Count == 0)
            {
                float emptyMaterialHeight = ValueRowHeight(
                    NoMaterialRestrictionMessage, controlsWidth);
                DrawRowLabel(AllowedMaterialsLabel, y, emptyMaterialHeight);
                Widgets.Label(
                    new Rect(controlsX, y, controlsWidth, emptyMaterialHeight),
                    NoMaterialRestrictionMessage);
                y += emptyMaterialHeight;
            }
            else
            {
                for (int i = 0; i < materialRows.Count; i++)
                {
                    ThingDef stuff = materialRows[i];
                    string stuffLabel = stuff.LabelCap.ToString();
                    float stuffRowHeight = CheckboxRowHeight(stuffLabel, controlsWidth);
                    bool allowed = workingCopy.allowedStuff != null &&
                        workingCopy.allowedStuff.Contains(stuff);
                    bool wasAllowed = allowed;
                    Widgets.CheckboxLabeled(
                        new Rect(controlsX, y, controlsWidth, stuffRowHeight),
                        stuffLabel,
                        ref allowed);
                    if (allowed != wasAllowed)
                    {
                        List<ThingDef> allowedStuff = CopyAllowedStuff(workingCopy.allowedStuff);
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

                        workingCopy.allowedStuff = allowedStuff;
                    }

                    y += stuffRowHeight;
                    if (i < materialRows.Count - 1)
                    {
                        y += RowGap;
                    }
                }
            }

            y += RowGap;
            DrawRowLabel(AllowedMaterialsLabel, y, ControlRowHeight);
            if (Widgets.ButtonText(
                    new Rect(controlsX, y, FooterButtonWidth, ControlRowHeight), AddMaterialLabel))
            {
                OpenMaterialMenu();
            }
        }

        private float ControlsHeight(
            float width,
            bool maintainStock,
            ProduceControlPreset preset)
        {
            float controlsWidth = width - LabelColumnWidth - LabelColumnGap;
            string title = PresetTitle(preset);
            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight(title, width);
            Text.Font = GameFont.Small;

            float height = titleHeight + SectionGap;
            height += RadioRowHeight(IndefiniteMode, controlsWidth) + RowGap;
            height += RadioRowHeight(MaintainMode, controlsWidth) + RowGap;
            if (maintainStock)
            {
                height += ControlRowHeight + RowGap;
                height += ControlRowHeight + RowGap;
            }

            height += SectionGap;
            height += SectionHeadingHeight(WorkersHeading, width) + RowGap;
            height += RadioRowHeight(AnyWorkerMode, controlsWidth) + RowGap;
            height += RadioRowHeight(SelectedWorkerMode, controlsWidth) + RowGap;

            // Include these rows even when restriction is off so selecting it cannot draw into the footer this frame.
            List<Pawn> availableWorkers = AvailableWorkerCandidates();
            List<Pawn> workerRows = WorkerCandidates(workingCopy, availableWorkers);
            for (int i = 0; i < workerRows.Count; i++)
            {
                height += CheckboxRowHeight(
                    WorkerLabel(workerRows[i], !availableWorkers.Contains(workerRows[i])),
                    controlsWidth) + RowGap;
            }

            height += ControlRowHeight + RowGap;
            height += SectionGap;
            height += SectionHeadingHeight(MaterialsHeading, width) + RowGap;

            List<ThingDef> materialRows = AllowedMaterialRows();
            if (materialRows.Count == 0)
            {
                height += ValueRowHeight(NoMaterialRestrictionMessage, controlsWidth);
            }
            else
            {
                for (int i = 0; i < materialRows.Count; i++)
                {
                    height += CheckboxRowHeight(
                        materialRows[i].LabelCap.ToString(), controlsWidth);
                    if (i < materialRows.Count - 1)
                    {
                        height += RowGap;
                    }
                }
            }

            height += RowGap;
            height += ControlRowHeight;
            return height;
        }

        private void OpenMaterialMenu()
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            if (map == null || component == null || preset == null || workingCopy == null)
            {
                Close();
                return;
            }

            List<ThingDef> options = AvailableMaterialOptions();
            if (options.Count == 0)
            {
                return;
            }

            List<FloatMenuOption> menuOptions = new List<FloatMenuOption>();
            for (int i = 0; i < options.Count; i++)
            {
                ThingDef selectedStuff = options[i];
                menuOptions.Add(new FloatMenuOption(
                    selectedStuff.LabelCap.ToString(),
                    () => AddMaterial(selectedStuff)));
            }

            Find.WindowStack.Add(new FloatMenu(menuOptions));
        }

        private void AddMaterial(ThingDef stuff)
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            if (map == null || component == null || preset == null || workingCopy == null || stuff == null)
            {
                Close();
                return;
            }

            if (workingCopy.allowedStuff == null)
            {
                workingCopy.allowedStuff = new List<ThingDef>();
            }

            if (!workingCopy.allowedStuff.Contains(stuff))
            {
                workingCopy.allowedStuff.Add(stuff);
            }
        }

        private void AcceptChanges()
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            if (map == null || component == null || preset == null || workingCopy == null)
            {
                Close();
                return;
            }

            // Unlike the concrete-loop dialog, this template must not save half-typed settings before Accept.
            component.ApplyPresetSettings(presetId, workingCopy);
            Close();
        }

        private void CancelChanges()
        {
            Close();
        }

        private ProduceControlPreset FindPreset()
        {
            ProduceLoopMapComponent component = ProduceLoopMapComponent.For(map);
            return component == null ? null : component.FindPreset(presetId);
        }

        private static ProduceControlPreset CopyPresetSettings(ProduceControlPreset preset)
        {
            if (preset == null)
            {
                return null;
            }

            return new ProduceControlPreset
            {
                id = preset.id,
                name = preset.name,
                targetCount = preset.targetCount,
                resumeBelow = preset.resumeBelow,
                restrictToSelectedWorkers = preset.restrictToSelectedWorkers,
                allowedWorkers = CopyAllowedWorkers(preset.allowedWorkers),
                minConstructionSkill = preset.minConstructionSkill,
                allowedStuff = CopyAllowedStuff(preset.allowedStuff)
            };
        }

        private void RefreshBuffers()
        {
            if (workingCopy == null)
            {
                return;
            }

            targetBuffer = workingCopy.targetCount.ToString();
            resumeBelowBuffer = workingCopy.EffectiveResumeBelow.ToString();
            minConstructionSkillBuffer = workingCopy.minConstructionSkill.ToString();
        }

        private void SetTargetCount(int value)
        {
            if (workingCopy == null)
            {
                return;
            }

            if (value <= 0)
            {
                workingCopy.targetCount = 0;
                workingCopy.resumeBelow = -1;
            }
            else
            {
                workingCopy.targetCount = Mathf.Clamp(value, MinimumTarget, TargetMaximum());
                if (workingCopy.resumeBelow >= workingCopy.targetCount)
                {
                    workingCopy.resumeBelow = workingCopy.targetCount - 1;
                }

                lastPositiveTarget = workingCopy.targetCount;
            }

            RefreshBuffers();
        }

        private List<ThingDef> AvailableMaterialOptions()
        {
            List<ThingDef> options = new List<ThingDef>();
            if (workingCopy == null)
            {
                return options;
            }

            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef def = allDefs[i];
                if (def != null && def.IsStuff &&
                    (workingCopy.allowedStuff == null || !workingCopy.allowedStuff.Contains(def)))
                {
                    options.Add(def);
                }
            }

            options.Sort(CompareThingDefsByLabel);
            return options;
        }

        private List<ThingDef> AllowedMaterialRows()
        {
            List<ThingDef> rows = CopyAllowedStuff(workingCopy == null
                ? null
                : workingCopy.allowedStuff);
            rows.Sort(CompareThingDefsByLabel);
            return rows;
        }

        private static int CompareThingDefsByLabel(ThingDef left, ThingDef right)
        {
            if (left == null)
            {
                return right == null ? 0 : -1;
            }

            if (right == null)
            {
                return 1;
            }

            int labelComparison = string.CompareOrdinal(
                left.LabelCap.ToString(), right.LabelCap.ToString());
            return labelComparison != 0
                ? labelComparison
                : string.CompareOrdinal(left.defName, right.defName);
        }

        private List<Pawn> AvailableWorkerCandidates()
        {
            List<Pawn> candidates = new List<Pawn>();
            if (map != null && map.mapPawns != null)
            {
                List<Pawn> freeColonists = map.mapPawns.FreeColonistsSpawned;
                if (freeColonists != null)
                {
                    for (int i = 0; i < freeColonists.Count; i++)
                    {
                        AddWorkerIfMissing(candidates, freeColonists[i]);
                    }
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
            ProduceControlPreset preset,
            List<Pawn> availableWorkers)
        {
            List<Pawn> candidates = availableWorkers == null
                ? new List<Pawn>()
                : new List<Pawn>(availableWorkers);
            if (preset?.allowedWorkers != null)
            {
                for (int i = 0; i < preset.allowedWorkers.Count; i++)
                {
                    AddWorkerIfMissing(candidates, preset.allowedWorkers[i]);
                }
            }

            candidates.Sort((left, right) =>
                string.CompareOrdinal(WorkerSortLabel(left), WorkerSortLabel(right)));
            return candidates;
        }

        private static void AddWorkerIfMissing(List<Pawn> workers, Pawn worker)
        {
            if (workers != null && worker != null && !workers.Contains(worker))
            {
                workers.Add(worker);
            }
        }

        private static string WorkerSortLabel(Pawn pawn)
        {
            return pawn?.LabelShortCap ?? string.Empty;
        }

        private static string WorkerLabel(Pawn pawn, bool unavailable)
        {
            string label = (pawn?.LabelShortCap ?? UnknownPawnLabel) +
                ConstructionLabelPrefix + ConstructionLevel(pawn) + ConstructionLabelSuffix;
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

        private static List<Pawn> CopyAllowedWorkers(List<Pawn> workers)
        {
            return workers == null ? new List<Pawn>() : new List<Pawn>(workers);
        }

        private static List<ThingDef> CopyAllowedStuff(List<ThingDef> stuffs)
        {
            List<ThingDef> copiedStuffs = stuffs == null
                ? new List<ThingDef>()
                : new List<ThingDef>(stuffs);
            copiedStuffs.RemoveAll(stuff => stuff == null);
            return copiedStuffs;
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

        private static float ContentWidth(float availableWidth)
        {
            return availableWidth - ContentLeft * 2f;
        }

        private static string PresetTitle(ProduceControlPreset preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.name))
            {
                return UnnamedPresetLabel;
            }

            return preset.name;
        }

        private static int TargetMaximum()
        {
            return Mathf.Max(MinimumTarget, IntercolonyMod.Settings.maxProduceTarget);
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
