using System;
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
        private const float FutureSectionsGap = 12f;

        private const string Title = "Produce controls";
        private const string IndefiniteMode = "Produce indefinitely";
        private const string MaintainMode = "Maintain stock";
        private const string WaitingStatus = "Waiting for stock to fall";
        private const string NotWaitingStatus = "Not waiting for stock to fall";

        private readonly Map map;
        private readonly IntVec3 cell;

        // This is UI memory only. It is deliberately not part of ProduceLoopRecord or the save state.
        private int lastPositiveTarget = 10;
        private string targetBuffer = "0";
        private string resumeBelowBuffer = "0";
        private int syncedTargetCount;
        private int syncedEffectiveResumeBelow;
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
                float contentHeight = ControlsHeight(contentWidth, true);
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
            float contentHeight = ControlsHeight(contentWidth, true);
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

            // Future Workers and Materials sections belong in this reserved gap in the next unit.
            // Do not add placeholder controls: unfinished settings must not look configurable.
            y += FutureSectionsGap;
        }

        private static void DrawRowLabel(string label, float rowY, float controlHeight)
        {
            float labelHeight = Text.CalcHeight(label, LabelColumnWidth);
            float labelY = rowY + (controlHeight - labelHeight) / 2f;
            Widgets.Label(new Rect(0f, labelY, LabelColumnWidth, labelHeight), label);
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

        private static float ControlsHeight(float width, bool maintainStock)
        {
            float controlsWidth = width - LabelColumnWidth - LabelColumnGap;
            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight(Title, width);
            Text.Font = GameFont.Small;

            float height = titleHeight + SectionGap;
            height += RadioRowHeight(IndefiniteMode, controlsWidth) + RowGap;
            height += RadioRowHeight(MaintainMode, controlsWidth) + RowGap;
            if (maintainStock)
            {
                height += ControlRowHeight + RowGap;
                height += ControlRowHeight + RowGap;
                height += StatusRowHeight(controlsWidth);
            }

            return height + FutureSectionsGap;
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
                syncedEffectiveResumeBelow != effectiveResumeBelow)
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
            syncedTargetCount = loop.targetCount;
            syncedEffectiveResumeBelow = loop.EffectiveResumeBelow;
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
