using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// One consent moment for vanilla apparel optimization removing an original bonded item.
    /// Closing without pressing either decision button leaves the contract pending.
    /// </summary>
    public sealed class Dialog_ApparelBondConsent : Window
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

        private const string Title = "Apparel change";
        private const string Consequence =
            "Keeping the gear forfeits that share of the refundable bond.";

        private readonly EmploymentContract contract;
        private readonly List<TermRow> rows;
        private readonly Action onClosed;
        private Vector2 bodyScroll;
        private bool closedNotified;

        public Dialog_ApparelBondConsent(
            EmploymentContract contract,
            Pawn pawn,
            Apparel apparel,
            int bondAtRisk,
            Action onClosed)
        {
            this.contract = contract;
            this.onClosed = onClosed;
            rows = new List<TermRow>
            {
                new TermRow("Worker", pawn?.LabelShortCap ?? contract?.workerName ?? "Unknown"),
                new TermRow("Apparel", apparel?.LabelCap ?? "Unknown apparel"),
                new TermRow("Bond at risk now", $"{Mathf.Max(0, bondAtRisk):N0} silver")
            };

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
                float titleHeight = Text.CalcHeight(Title, contentWidth);
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
            float titleHeight = Text.CalcHeight(Title, inRect.width);
            DrawMeasuredLabel(new Rect(0f, 0f, inRect.width, titleHeight), Title);

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
                DrawBody(bodyRect.width, bodyRect.y);
            }
            else
            {
                float viewWidth = ContentWidth(bodyRect.width);
                Rect viewRect = new Rect(0f, 0f, viewWidth, BodyHeight(viewWidth));
                Widgets.BeginScrollView(bodyRect, ref bodyScroll, viewRect);
                DrawBody(viewWidth, 0f);
                Widgets.EndScrollView();
            }

            float buttonWidth = (inRect.width - ButtonGap) / 2f;
            if (Widgets.ButtonText(
                    new Rect(0f, buttonsTop, buttonWidth, ButtonHeight),
                    "Keep issued apparel"))
            {
                SetDecision(ApparelBondDecision.Denied);
                return;
            }

            if (Widgets.ButtonText(
                    new Rect(buttonWidth + ButtonGap, buttonsTop, buttonWidth, ButtonHeight),
                    "Allow change"))
            {
                SetDecision(ApparelBondDecision.Allowed);
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

        private void SetDecision(ApparelBondDecision decision)
        {
            if (contract != null)
            {
                contract.apparelBondDecision = decision;
            }

            Close();
        }

        private void DrawBody(float width, float startY)
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

            y += RowGap;
            DrawMeasuredLabel(
                new Rect(0f, y, width, Text.CalcHeight(Consequence, width)), Consequence);
        }

        private float BodyHeight(float width)
        {
            float height = MeasureRows(width) + RowGap;
            return height + Text.CalcHeight(Consequence, width);
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
