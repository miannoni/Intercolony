using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Intercolony
{
    /// <summary>Shared selectable title-and-explanation row used for labor terms.</summary>
    public static class LaborOptionRows
    {
        public static float Draw(float width, float y, string title, string explanation, bool selected,
            Action choose)
        {
            float textWidth = width - 40f;
            float titleHeight = Text.CalcHeight(title, textWidth);
            float explanationHeight = Text.CalcHeight(explanation, textWidth);
            float rowHeight = Height(title, explanation, width);
            Rect row = new Rect(0f, y, width, rowHeight);

            if (selected)
            {
                Widgets.DrawHighlightSelected(row);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(row);
            }

            Widgets.RadioButton(new Vector2(4f, y + (rowHeight - 24f) / 2f), selected);
            Widgets.Label(new Rect(34f, y + 2f, textWidth, titleHeight), title);

            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(34f, y + 4f + titleHeight, textWidth, explanationHeight),
                explanation);
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(row) && !selected)
            {
                choose();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            return y + rowHeight;
        }

        public static float Height(string title, string explanation, float width)
        {
            float textWidth = width - 40f;
            return 8f + Text.CalcHeight(title, textWidth) + Text.CalcHeight(explanation, textWidth);
        }
    }
}
