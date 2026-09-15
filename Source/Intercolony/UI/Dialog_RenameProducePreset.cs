using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public class Dialog_RenameProducePreset : Window
    {
        private const float WindowWidth = 460f;
        private const float WindowMargin = 18f;
        private const float LabelColumnWidth = 100f;
        private const float LabelColumnGap = 12f;
        private const float ControlVerticalPadding = 8f;
        private const float FooterGap = 12f;
        private const float FooterButtonWidth = 120f;
        private const float FooterButtonGap = 8f;

        private const string NameLabel = "Preset name";
        private const string AcceptLabel = "Accept";
        private const string CancelLabel = "Cancel";

        private readonly Map map;
        private readonly int presetId;
        private string nameBuffer;

        public Dialog_RenameProducePreset(Map map, int presetId)
        {
            this.map = map;
            this.presetId = presetId;

            ProduceLoopMapComponent component = map == null
                ? null
                : ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            nameBuffer = preset == null || preset.name == null
                ? string.Empty
                : preset.name;

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                Text.Font = GameFont.Small;
                float rowHeight = NameRowHeight();
                float footerHeight = FooterHeight();
                float contentHeight = rowHeight + FooterGap + footerHeight;
                return new Vector2(
                    WindowWidth,
                    WindowMargin * 2f + contentHeight);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            ProduceLoopMapComponent component = map == null
                ? null
                : ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            if (map == null || component == null || preset == null)
            {
                // A popup can outlive its record, so close it instead of drawing against stale state.
                Close();
                return;
            }

            float rowHeight = NameRowHeight();
            float labelHeight = Text.CalcHeight(NameLabel, LabelColumnWidth);
            float labelY = (rowHeight - labelHeight) / 2f;
            Widgets.Label(
                new Rect(0f, labelY, LabelColumnWidth, labelHeight),
                NameLabel);

            float fieldHeight = FieldHeight();
            float fieldWidth = Mathf.Max(
                1f,
                inRect.width - LabelColumnWidth - LabelColumnGap);
            nameBuffer = Widgets.TextField(
                new Rect(LabelColumnWidth + LabelColumnGap, 0f, fieldWidth, fieldHeight),
                nameBuffer);

            float footerHeight = FooterHeight();
            float footerY = inRect.height - footerHeight;
            float buttonWidth = Mathf.Min(
                FooterButtonWidth,
                Mathf.Max(1f, (inRect.width - FooterButtonGap) / 2f));
            float buttonsWidth = buttonWidth * 2f + FooterButtonGap;
            float buttonsX = (inRect.width - buttonsWidth) / 2f;
            if (Widgets.ButtonText(
                    new Rect(buttonsX, footerY, buttonWidth, footerHeight),
                    AcceptLabel))
            {
                AcceptRename();
                return;
            }

            if (Widgets.ButtonText(
                    new Rect(buttonsX + buttonWidth + FooterButtonGap,
                        footerY,
                        buttonWidth,
                        footerHeight),
                    CancelLabel))
            {
                Close();
            }
        }

        public override void OnAcceptKeyPressed()
        {
            AcceptRename();
            if (Event.current != null)
            {
                Event.current.Use();
            }
        }

        public override void OnCancelKeyPressed()
        {
            Close();
            if (Event.current != null)
            {
                Event.current.Use();
            }
        }

        private void AcceptRename()
        {
            ProduceLoopMapComponent component = map == null
                ? null
                : ProduceLoopMapComponent.For(map);
            ProduceControlPreset preset = component == null
                ? null
                : component.FindPreset(presetId);
            if (map == null || component == null || preset == null)
            {
                Close();
                return;
            }

            string reason;
            if (component.TryRenamePreset(presetId, nameBuffer, out reason))
            {
                Close();
                return;
            }

            Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
        }

        private static float NameRowHeight()
        {
            float labelHeight = Text.CalcHeight(NameLabel, LabelColumnWidth);
            return Mathf.Max(labelHeight, FieldHeight());
        }

        private static float FieldHeight()
        {
            return Text.LineHeight + ControlVerticalPadding;
        }

        private static float FooterHeight()
        {
            float acceptHeight = ButtonHeight(AcceptLabel);
            float cancelHeight = ButtonHeight(CancelLabel);
            return Mathf.Max(acceptHeight, cancelHeight);
        }

        private static float ButtonHeight(string label)
        {
            return Text.CalcHeight(label, FooterButtonWidth) + ControlVerticalPadding;
        }
    }
}
