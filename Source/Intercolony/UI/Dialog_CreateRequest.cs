using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Item selection for a purchase request (DESIGN.md §19, §103 "item selection; quantity").
    ///
    /// A searchable list rather than a float menu: there are 400+ tradable defs, and a nested
    /// menu of that size is unusable. §19 is explicit that the purchase side is "deliberately
    /// not a store catalog" — the player states a need, so this asks for exactly that.
    /// </summary>
    public class Dialog_CreateRequest : Window
    {
        private readonly IntercolonyWorldComponent state;

        private string searchText = "";
        private Vector2 scroll;
        private ThingDef selected;
        private string quantityBuffer = "40";
        private int quantity = 40;
        private string deadlineBuffer = "15";
        private int deadlineDays = 15;

        private List<ThingDef> cachedMatches;
        private string cachedSearch;

        public Dialog_CreateRequest(IntercolonyWorldComponent state)
        {
            this.state = state;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(560f, 620f);

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 34f), "Request goods");
            y += 38f;
            Text.Font = GameFont.Small;

            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(0f, y, inRect.width, 40f),
                "State what you need. Suppliers may answer with full or partial quotes — or not at all.");
            GUI.color = Color.white;
            y += 42f;

            Rect searchRect = new Rect(0f, y, inRect.width, 28f);
            string newSearch = Widgets.TextField(searchRect, searchText);
            if (newSearch != searchText)
            {
                searchText = newSearch;
                cachedMatches = null;
            }

            y += 34f;

            List<ThingDef> matches = Matches();
            Rect listRect = new Rect(0f, y, inRect.width, inRect.height - y - 150f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, matches.Count * 26f);

            Widgets.BeginScrollView(listRect, ref scroll, viewRect);
            float rowY = 0f;
            foreach (ThingDef def in matches)
            {
                Rect row = new Rect(0f, rowY, viewRect.width, 26f);
                if (selected == def)
                {
                    Widgets.DrawHighlightSelected(row);
                }

                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(row.x + 4f, row.y + 2f, row.width - 90f, 24f), def.LabelCap);

                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Widgets.Label(new Rect(row.xMax - 84f, row.y + 2f, 80f, 24f),
                    $"~{def.BaseMarketValue:F0}");
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(row))
                {
                    selected = def;
                }

                rowY += 26f;
            }

            Widgets.EndScrollView();

            float bottom = inRect.height - 108f;

            Widgets.Label(new Rect(0f, bottom, 120f, 28f), "Quantity:");
            Widgets.TextFieldNumeric(new Rect(124f, bottom, 100f, 28f), ref quantity, ref quantityBuffer, 1, 5000);

            Widgets.Label(new Rect(240f, bottom, 140f, 28f), "Wanted within:");
            Widgets.TextFieldNumeric(new Rect(384f, bottom, 70f, 28f), ref deadlineDays, ref deadlineBuffer, 1, 60);
            Widgets.Label(new Rect(458f, bottom, 60f, 28f), "days");

            bottom += 36f;
            if (selected != null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                Widgets.Label(new Rect(0f, bottom, inRect.width, 24f),
                    $"Requesting {quantity}x {selected.LabelCap} — roughly " +
                    $"{Mathf.RoundToInt(selected.BaseMarketValue * quantity * 1.3f)} silver if anyone answers.");
                GUI.color = Color.white;
            }

            bottom += 30f;
            Rect sendRect = new Rect(0f, bottom, 180f, 34f);
            if (selected == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Widgets.Label(sendRect, "Select an item first.");
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonText(sendRect, "Send request"))
            {
                RfqService.CreateRequest(state, selected, null, quantity, deadlineDays);
                Close();
            }

            Rect cancelRect = new Rect(inRect.width - 120f, bottom, 110f, 34f);
            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Close();
            }
        }

        /// <summary>
        /// Filtered candidates, cached per search string. Recomputing over 400+ defs on every
        /// GUI event would stutter for the same reason the Find Buyer stock scan did (§84).
        /// </summary>
        private List<ThingDef> Matches()
        {
            if (cachedMatches != null && cachedSearch == searchText)
            {
                return cachedMatches;
            }

            cachedMatches = new List<ThingDef>();
            cachedSearch = searchText;

            string needle = searchText?.Trim().ToLowerInvariant() ?? "";
            foreach (ThingDef def in IntercolonyProductClassifier.TradableDefs)
            {
                if (needle.Length > 0 && def.label != null &&
                    !def.label.ToLowerInvariant().Contains(needle))
                {
                    continue;
                }

                cachedMatches.Add(def);
            }

            cachedMatches.Sort((a, b) => string.Compare(
                a.label ?? "", b.label ?? "", System.StringComparison.CurrentCultureIgnoreCase));
            return cachedMatches;
        }
    }
}
