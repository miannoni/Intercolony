using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Intercolony
{
    /// <summary>
    /// The rudimentary market tab (DESIGN.md §52, §53, §97). Lists every live opportunity so
    /// the player can inspect what counterparties want, at what price, and for how long.
    ///
    /// Deliberately read-only: accepting an opportunity turns it into a binding Sales Order,
    /// which is Phase 5 (§98). Nothing here commits the player to anything.
    /// </summary>
    public class MainTabWindow_Intercolony : MainTabWindow
    {
        private Vector2 scrollPosition;

        private const float RowHeight = 30f;
        private const float HeaderHeight = 26f;

        /// <summary>Column indices, matching <see cref="ColumnLabels"/>.</summary>
        private enum Column
        {
            Buyer = 0,
            Item = 1,
            Quantity = 2,
            UnitPrice = 3,
            TotalPrice = 4,
            Distance = 5,
            Expires = 6,
            Deadline = 7
        }

        /// <summary>
        /// Default sort is by soonest expiry: the listing's most time-critical information,
        /// and the one a player is most likely to act on.
        /// </summary>
        private Column sortColumn = Column.Expires;

        private bool sortDescending;

        public override Vector2 RequestedTabSize => new Vector2(920f, 560f);

        public override void DoWindowContents(Rect inRect)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                Widgets.Label(inRect, "Intercolony state is unavailable.");
                return;
            }

            int totalAvailable = 0;
            List<MarketOpportunity> live = new List<MarketOpportunity>();
            foreach (MarketOpportunity opportunity in state.Opportunities)
            {
                if (!opportunity.IsAvailable)
                {
                    continue;
                }

                totalAvailable++;
                if (PassesDistanceFilter(opportunity, state.MaxMarketDistance))
                {
                    live.Add(opportunity);
                }
            }

            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 34f), "Market opportunities");
            y += 38f;
            Text.Font = GameFont.Small;

            y = DrawFilterRow(inRect, y, state, live.Count, totalAvailable);

            if (live.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, inRect.width, 60f),
                    totalAvailable == 0
                        ? "No settlement is currently asking for anything.\n" +
                          "Demand is refreshed periodically as the world turns."
                        : $"All {totalAvailable} current offers are beyond your distance limit.\n" +
                          "Raise the limit above to see them.");
                GUI.color = Color.white;
                return;
            }

            Sort(live);

            DrawHeaderRow(new Rect(0f, y, inRect.width - 16f, HeaderHeight));
            y += HeaderHeight;
            Widgets.DrawLineHorizontal(0f, y, inRect.width);
            y += 2f;

            Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, live.Count * RowHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float rowY = 0f;
            for (int i = 0; i < live.Count; i++)
            {
                DrawRow(new Rect(0f, rowY, viewRect.width, RowHeight), live[i], i);
                rowY += RowHeight;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Column layout, as fractions of the available width. Kept in one place so the header
        /// and the rows cannot drift apart.
        /// </summary>
        private static readonly float[] ColumnWidths =
            { 0.18f, 0.20f, 0.08f, 0.09f, 0.11f, 0.10f, 0.09f, 0.15f };

        private static readonly string[] ColumnLabels =
            { "Buyer", "Wants", "Qty", "Unit", "Total", "Distance", "Expires", "Delivery deadline" };

        /// <summary>
        /// Distance sort key. Unknown distance (-1) must sort as "furthest", not "nearest",
        /// or migrated pre-distance opportunities would masquerade as being next door.
        /// </summary>
        private static float SortableDistance(MarketOpportunity opportunity)
        {
            return opportunity.distanceTiles < 0f ? float.MaxValue : opportunity.distanceTiles;
        }

        /// <summary>
        /// An opportunity with unknown distance is always shown. Hiding it would mean a save
        /// migrated from before distances were recorded silently loses listings.
        /// </summary>
        private static bool PassesDistanceFilter(MarketOpportunity opportunity, float maxDistance)
        {
            if (maxDistance >= IntercolonyWorldComponent.NoDistanceLimit ||
                opportunity.distanceTiles < 0f)
            {
                return true;
            }

            return opportunity.distanceTiles <= maxDistance;
        }

        /// <summary>
        /// Distance filter (DESIGN.md §53 "Potential filters: ... distance", §66 "maximum
        /// market distance"). A far-off buyer is not useless — §48 is explicit that distant
        /// settlements must stay relevant — but an early colony cannot cross the planet, so
        /// the player needs to narrow the list to what they can actually service.
        /// </summary>
        private float DrawFilterRow(Rect inRect, float y, IntercolonyWorldComponent state,
            int shown, int totalAvailable)
        {
            Rect row = new Rect(0f, y, inRect.width, 30f);

            Rect labelRect = new Rect(0f, row.y + 4f, 130f, 24f);
            Widgets.Label(labelRect, "Max distance:");

            Rect sliderRect = new Rect(labelRect.xMax + 4f, row.y + 6f, 260f, 20f);
            float current = state.MaxMarketDistance;
            float slider = Widgets.HorizontalSlider(
                sliderRect,
                Mathf.Min(current, MaxFilterTiles),
                0f,
                MaxFilterTiles,
                middleAlignment: false,
                null,
                null,
                null,
                roundTo: 5f);

            // The top of the slider means "no limit" rather than exactly MaxFilterTiles, so
            // the player can always get back to seeing everything.
            float chosen = slider >= MaxFilterTiles
                ? IntercolonyWorldComponent.NoDistanceLimit
                : slider;

            if (!Mathf.Approximately(chosen, current))
            {
                state.MaxMarketDistance = chosen;
            }

            Rect valueRect = new Rect(sliderRect.xMax + 8f, row.y + 4f, 150f, 24f);
            Widgets.Label(valueRect, chosen >= IntercolonyWorldComponent.NoDistanceLimit
                ? "no limit"
                : $"{chosen:F0} tiles");

            Rect countRect = new Rect(valueRect.xMax + 8f, row.y + 4f, inRect.width - valueRect.xMax - 12f, 24f);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(countRect, shown == totalAvailable
                ? $"{shown} offers"
                : $"{shown} of {totalAvailable} offers");
            GUI.color = Color.white;

            return row.yMax + 4f;
        }

        /// <summary>Upper end of the filter slider; beyond this it means "no limit".</summary>
        private const float MaxFilterTiles = 200f;

        /// <summary>
        /// Clickable headers, matching the sorting convention players already know from
        /// vanilla tables: click to sort, click the active column again to reverse.
        /// </summary>
        private void DrawHeaderRow(Rect rect)
        {
            float x = 0f;
            for (int i = 0; i < ColumnLabels.Length; i++)
            {
                float w = rect.width * ColumnWidths[i];
                Rect cell = new Rect(x, rect.y, w, rect.height);
                bool active = (int)sortColumn == i;

                Widgets.DrawHighlightIfMouseover(cell);

                GUI.color = active ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                string arrow = active ? (sortDescending ? " v" : " ^") : "";
                Widgets.Label(cell, ColumnLabels[i] + arrow);
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(cell))
                {
                    if (active)
                    {
                        sortDescending = !sortDescending;
                    }
                    else
                    {
                        sortColumn = (Column)i;

                        // Text reads naturally A-Z; numbers and money are almost always
                        // wanted biggest-first, so pick the useful default per column type.
                        sortDescending = i >= (int)Column.Quantity && i <= (int)Column.TotalPrice;
                    }

                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }

                x += w;
            }
        }

        private void Sort(List<MarketOpportunity> list)
        {
            Comparison<MarketOpportunity> comparison;
            switch (sortColumn)
            {
                case Column.Buyer:
                    comparison = (a, b) => string.Compare(
                        a.settlementName, b.settlementName, StringComparison.CurrentCultureIgnoreCase);
                    break;
                case Column.Item:
                    comparison = (a, b) => string.Compare(
                        a.thingDef?.label ?? "", b.thingDef?.label ?? "", StringComparison.CurrentCultureIgnoreCase);
                    break;
                case Column.Quantity:
                    comparison = (a, b) => a.quantity.CompareTo(b.quantity);
                    break;
                case Column.UnitPrice:
                    comparison = (a, b) => a.unitPrice.CompareTo(b.unitPrice);
                    break;
                case Column.TotalPrice:
                    comparison = (a, b) => a.TotalPrice.CompareTo(b.TotalPrice);
                    break;
                case Column.Distance:
                    // Unknown distance sorts last rather than pretending to be zero.
                    comparison = (a, b) => SortableDistance(a).CompareTo(SortableDistance(b));
                    break;
                case Column.Deadline:
                    comparison = (a, b) => a.deadlineDays.CompareTo(b.deadlineDays);
                    break;
                default:
                    comparison = (a, b) => a.expiryTick.CompareTo(b.expiryTick);
                    break;
            }

            // Tie-break on id so equal keys keep a stable, non-jittering order between frames.
            list.Sort((a, b) =>
            {
                int result = comparison(a, b);
                if (result != 0)
                {
                    return sortDescending ? -result : result;
                }

                return a.id.CompareTo(b.id);
            });
        }

        private void DrawRow(Rect rect, MarketOpportunity opportunity, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);

            // §47: the player should understand why an offer is attractive. The full factor
            // breakdown was computed once at generation time and stored on the opportunity.
            TooltipHandler.TipRegion(rect, opportunity.priceExplanation);

            float[] w = new float[ColumnWidths.Length];
            for (int i = 0; i < ColumnWidths.Length; i++)
            {
                w[i] = rect.width * ColumnWidths[i];
            }

            Rect Cell(int i)
            {
                float cellX = 0f;
                for (int k = 0; k < i; k++)
                {
                    cellX += w[k];
                }

                return new Rect(cellX, rect.y + 4f, w[i], rect.height - 4f);
            }

            Widgets.Label(Cell(0), opportunity.settlementName);
            Widgets.Label(Cell(1), opportunity.thingDef?.LabelCap.ToString() ?? "<missing>");
            Widgets.Label(Cell(2), opportunity.quantity.ToString());
            Widgets.Label(Cell(3), opportunity.unitPrice.ToString("F2"));
            Widgets.Label(Cell(4), opportunity.TotalPrice.ToString());
            Widgets.Label(Cell(5), opportunity.distanceTiles < 0f
                ? "?"
                : $"{opportunity.distanceTiles:F0} t");

            float days = opportunity.DaysRemaining;
            GUI.color = days < 1.5f ? Color.yellow : Color.white;
            Widgets.Label(Cell(6), $"{days:F1}d");
            GUI.color = Color.white;

            Widgets.Label(Cell(7), $"{opportunity.deadlineDays}d after accepting");
        }
    }
}
