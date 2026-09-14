using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Confirms a player-forced removal of original employee gear. It deliberately does not write
    /// ApparelBondDecision: this is a one-action approval, not standing consent for optimization.
    /// </summary>
    public sealed class Dialog_ForcedApparelReleaseConsent : Window
    {
        private const float WindowWidth = 520f;
        private const float WindowMargin = 18f;
        private const float TitleBodyGap = 8f;
        private const float BodyButtonsGap = 10f;
        private const float ButtonHeight = 36f;
        private const float ButtonGap = 8f;
        private const float LabelColumnWidth = 150f;
        private const float ValueColumnGap = 8f;
        private const float RowGap = 5f;
        private const float ScrollbarWidth = 16f;
        private const float MaxScreenHeightFraction = 0.7f;

        private readonly string title;
        private readonly string confirmLabel;
        private readonly List<TermRow> rows;
        private readonly Action onConfirmed;
        private readonly Action onClosed;
        private Vector2 bodyScroll;
        private bool closedNotified;

        public Dialog_ForcedApparelReleaseConsent(
            Pawn pawn,
            string itemLabel,
            int bondAtRisk,
            Action onConfirmed,
            Action onClosed)
            : this(
                "Remove employee gear?",
                "Remove and forfeit share",
                new List<TermRow>
                {
                    new TermRow("Worker", pawn?.LabelShortCap ?? "Unknown worker"),
                    new TermRow("Item", itemLabel ?? "Unknown gear"),
                    new TermRow("Bond share at risk", $"{Mathf.Max(0, bondAtRisk):N0} silver")
                },
                onConfirmed,
                onClosed)
        {
        }

        public Dialog_ForcedApparelReleaseConsent(
            Pawn pawn,
            int totalBondAtRisk,
            Action onConfirmed,
            Action onClosed)
            : this(
                "Strip employee gear?",
                "Strip and forfeit bond",
                new List<TermRow>
                {
                    new TermRow("Worker", pawn?.LabelShortCap ?? "Unknown worker"),
                    new TermRow("Total bond at risk", $"{Mathf.Max(0, totalBondAtRisk):N0} silver")
                },
                onConfirmed,
                onClosed)
        {
        }

        private Dialog_ForcedApparelReleaseConsent(
            string title,
            string confirmLabel,
            List<TermRow> rows,
            Action onConfirmed,
            Action onClosed)
        {
            this.title = title;
            this.confirmLabel = confirmLabel;
            this.rows = rows ?? new List<TermRow>();
            this.onConfirmed = onConfirmed;
            this.onClosed = onClosed;

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
                float titleHeight = Text.CalcHeight(title, contentWidth);
                float bodyHeight = BodyHeight(contentWidth);
                float fixedHeight = WindowMargin * 2f + titleHeight + TitleBodyGap +
                                    BodyButtonsGap + ButtonHeight;
                float desiredHeight = fixedHeight + bodyHeight;
                float maximumHeight = Mathf.Max(
                    fixedHeight + Text.LineHeight,
                    UI.screenHeight * MaxScreenHeightFraction);
                return new Vector2(
                    WindowWidth,
                    Mathf.Min(Mathf.Max(fixedHeight + Text.LineHeight, desiredHeight), maximumHeight));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            float titleHeight = Text.CalcHeight(title, inRect.width);
            DrawMeasuredLabel(new Rect(0f, 0f, inRect.width, titleHeight), title);

            float bodyTop = titleHeight + TitleBodyGap;
            float buttonsTop = inRect.height - ButtonHeight;
            Rect bodyRect = new Rect(
                0f,
                bodyTop,
                inRect.width,
                Mathf.Max(1f, buttonsTop - bodyTop - BodyButtonsGap));
            float contentHeight = BodyHeight(bodyRect.width);

            if (contentHeight <= bodyRect.height)
            {
                bodyScroll = Vector2.zero;
                DrawRows(bodyRect.width, bodyRect.y);
            }
            else
            {
                float viewWidth = ContentWidth(bodyRect.width);
                Rect viewRect = new Rect(0f, 0f, viewWidth, BodyHeight(viewWidth));
                Widgets.BeginScrollView(bodyRect, ref bodyScroll, viewRect);
                DrawRows(viewWidth, 0f);
                Widgets.EndScrollView();
            }

            float buttonWidth = (inRect.width - ButtonGap) / 2f;
            if (Widgets.ButtonText(
                    new Rect(0f, buttonsTop, buttonWidth, ButtonHeight),
                    "Keep gear"))
            {
                Close();
                return;
            }

            if (Widgets.ButtonText(
                    new Rect(buttonWidth + ButtonGap, buttonsTop, buttonWidth, ButtonHeight),
                    confirmLabel))
            {
                onConfirmed?.Invoke();
                Close();
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            if (!closedNotified)
            {
                closedNotified = true;
                onClosed?.Invoke();
            }
        }

        private void DrawRows(float width, float startY)
        {
            float y = startY;
            for (int i = 0; i < rows.Count; i++)
            {
                TermRow row = rows[i];
                float rowHeight = RowHeight(row, width);
                DrawMeasuredLabel(new Rect(0f, y, LabelColumnWidth, rowHeight), row.label);
                DrawMeasuredLabel(
                    new Rect(LabelColumnWidth + ValueColumnGap, y,
                        ValueWidth(width), rowHeight),
                    row.value);
                y += rowHeight;
                if (i < rows.Count - 1)
                {
                    y += RowGap;
                }
            }
        }

        private float BodyHeight(float width)
        {
            return MeasureRows(width);
        }

        private float MeasureRows(float width)
        {
            float height = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                height += RowHeight(rows[i], width);
                if (i < rows.Count - 1)
                {
                    height += RowGap;
                }
            }

            return height;
        }

        private static float RowHeight(TermRow row, float width)
        {
            return Mathf.Max(
                Text.LineHeight,
                Text.CalcHeight(row.label ?? "", LabelColumnWidth),
                Text.CalcHeight(row.value ?? "", ValueWidth(width)));
        }

        private static float ValueWidth(float width)
        {
            return Mathf.Max(1f, width - LabelColumnWidth - ValueColumnGap);
        }

        private static float ContentWidth(float width)
        {
            return Mathf.Max(1f, width - ScrollbarWidth);
        }

        private static void DrawMeasuredLabel(Rect rect, string text)
        {
            string value = text ?? "";
            float measuredHeight = Text.CalcHeight(value, rect.width);
            Widgets.Label(
                new Rect(rect.x, rect.y, rect.width, Mathf.Max(rect.height, measuredHeight)),
                value);
        }
    }
}
