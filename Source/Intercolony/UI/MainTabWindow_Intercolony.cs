using System;
using System.Collections.Generic;
using System.Text;
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
            Deadline = 7,

            /// <summary>Action column. Not sortable; exists so Accept has its own space.</summary>
            Accept = 8
        }

        /// <summary>
        /// Default sort is by soonest expiry: the listing's most time-critical information,
        /// and the one a player is most likely to act on.
        /// </summary>
        private Column sortColumn = Column.Expires;

        private bool sortDescending;

        public override Vector2 RequestedTabSize => new Vector2(920f, 560f);

        /// <summary>Which top-level view is showing (DESIGN.md §52, §53, §54).</summary>
        private enum Tab
        {
            Market,
            Orders,
            FindBuyer
        }

        private Tab tab = Tab.Market;

        private Vector2 ordersScroll;

        /// <summary>Category filter (§53, §101). Null means "all categories".</summary>
        private IntercolonyProductCategory? categoryFilter;

        // --- Find Buyer (§12, §102) ---
        private Vector2 stockScroll;
        private Vector2 buyerScroll;
        private ThingDef selectedStockDef;
        private int selectedStockCount;

        /// <summary>How much of the selected stock to offer. Drives saturation pricing (§13).</summary>
        private int sellQuantity;

        private List<BuyerOffer> findBuyerCache;

        /// <summary>
        /// Colony stock is cached and only rebuilt on demand.
        ///
        /// <see cref="FindBuyerService.ColonyStock"/> walks every Thing on the map. GUI code
        /// runs at least twice per frame (layout and repaint), so calling it unconditionally
        /// scanned a developed colony's entire thing list ~120 times a second and tanked the
        /// frame rate. Nothing here needs to be live to the tick.
        /// </summary>
        private List<KeyValuePair<ThingDef, int>> stockCache;

        private enum BuyerColumn
        {
            Buyer = 0,
            MaxQuantity = 1,
            UnitPrice = 2,
            Total = 3,
            Distance = 4
        }

        private BuyerColumn buyerSortColumn = BuyerColumn.Total;
        private bool buyerSortDescending = true;

        /// <summary>Minimum total value filter (§53 "minimum value").</summary>
        private int minValueFilter;

        public override void DoWindowContents(Rect inRect)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                Widgets.Label(inRect, "Intercolony state is unavailable.");
                return;
            }

            float tabY = DrawTabSelector(inRect, state);
            Rect body = new Rect(0f, tabY, inRect.width, inRect.height - tabY);

            if (tab == Tab.Orders)
            {
                DrawOrders(body, state);
                return;
            }

            if (tab == Tab.FindBuyer)
            {
                DrawFindBuyer(body, state);
                return;
            }

            DrawMarket(body, state);
        }

        private float DrawTabSelector(Rect inRect, IntercolonyWorldComponent state)
        {
            const float ButtonWidth = 150f;
            const float ButtonHeight = 30f;

            Rect marketRect = new Rect(0f, 0f, ButtonWidth, ButtonHeight);
            Rect ordersRect = new Rect(ButtonWidth + 6f, 0f, ButtonWidth, ButtonHeight);

            int open = state.OpenOrderCount;
            if (Widgets.ButtonText(marketRect, "Market", drawBackground: tab != Tab.Market))
            {
                tab = Tab.Market;
            }

            if (Widgets.ButtonText(ordersRect, open > 0 ? $"Orders ({open})" : "Orders",
                    drawBackground: tab != Tab.Orders))
            {
                tab = Tab.Orders;
            }

            Rect findRect = new Rect(ordersRect.xMax + 6f, 0f, ButtonWidth, ButtonHeight);
            if (Widgets.ButtonText(findRect, "Find buyer", drawBackground: tab != Tab.FindBuyer))
            {
                tab = Tab.FindBuyer;

                // Re-scan once on entry so the list is current without being live.
                stockCache = null;
                findBuyerCache = null;
            }

            return ButtonHeight + 8f;
        }

        private void DrawMarket(Rect inRect, IntercolonyWorldComponent state)
        {
            int totalAvailable = 0;
            List<MarketOpportunity> live = new List<MarketOpportunity>();
            foreach (MarketOpportunity opportunity in state.Opportunities)
            {
                if (!opportunity.IsAvailable)
                {
                    continue;
                }

                totalAvailable++;
                if (PassesFilters(opportunity, state.MaxMarketDistance))
                {
                    live.Add(opportunity);
                }
            }

            // inRect starts below the tab selector, so y must too.
            float y = inRect.y;

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

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
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
            { 0.16f, 0.23f, 0.06f, 0.08f, 0.09f, 0.08f, 0.08f, 0.10f, 0.12f };

        /// <summary>
        /// Headers are short because the column has to hold the value, not the explanation.
        /// "Delivery deadline" with "11d after accepting" underneath did not fit and ran under
        /// the Accept button; the full wording now lives in the row tooltip.
        /// </summary>
        private static readonly string[] ColumnLabels =
            { "Buyer", "Wants", "Qty", "Unit", "Total", "Dist", "Expires", "Deadline", "" };

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

        /// <summary>All active filters (§53, §101). A listing must satisfy every one.</summary>
        private bool PassesFilters(MarketOpportunity opportunity, float maxDistance)
        {
            if (!PassesDistanceFilter(opportunity, maxDistance))
            {
                return false;
            }

            if (minValueFilter > 0 && opportunity.TotalPrice < minValueFilter)
            {
                return false;
            }

            if (categoryFilter.HasValue &&
                IntercolonyProductClassifier.Classify(opportunity.thingDef) != categoryFilter.Value)
            {
                return false;
            }

            return true;
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

            Rect countRect = new Rect(valueRect.xMax + 8f, row.y + 4f, 160f, 24f);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(countRect, shown == totalAvailable
                ? $"{shown} offers"
                : $"{shown} of {totalAvailable} offers");
            GUI.color = Color.white;

            // Second filter row: category and minimum value (§53, §101 "filters").
            Rect row2 = new Rect(0f, row.yMax + 2f, inRect.width, 30f);

            Widgets.Label(new Rect(0f, row2.y + 4f, 130f, 24f), "Category:");
            Rect categoryRect = new Rect(134f, row2.y + 3f, 170f, 26f);
            string categoryLabel = categoryFilter.HasValue
                ? categoryFilter.Value.Label()
                : "all categories";
            if (Widgets.ButtonText(categoryRect, categoryLabel))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("all categories", () => categoryFilter = null)
                };

                foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
                {
                    IntercolonyProductCategory local = category;
                    options.Add(new FloatMenuOption(local.Label(), () => categoryFilter = local));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }

            Widgets.Label(new Rect(categoryRect.xMax + 12f, row2.y + 4f, 110f, 24f), "Min value:");
            Rect minValueRect = new Rect(categoryRect.xMax + 122f, row2.y + 6f, 200f, 20f);
            minValueFilter = Mathf.RoundToInt(Widgets.HorizontalSlider(
                minValueRect, minValueFilter, 0f, MaxMinValueFilter,
                middleAlignment: false, null, null, null, roundTo: 100f));

            Widgets.Label(new Rect(minValueRect.xMax + 8f, row2.y + 4f, 150f, 24f),
                minValueFilter <= 0 ? "any value" : $"{minValueFilter}+ silver");

            return row2.yMax + 4f;
        }

        /// <summary>Upper end of the minimum-value slider.</summary>
        private const float MaxMinValueFilter = 5000f;

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
                Rect cell = new Rect(x, rect.y, w - 4f, rect.height);

                // The action column has no value to sort on; clicking it would set a sort
                // column with no comparison behind it.
                if (i == (int)Column.Accept)
                {
                    x += w;
                    continue;
                }

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
            // The deadline wording lives here rather than in the column, which is too narrow
            // to hold an explanation without colliding with the Accept button.
            TooltipHandler.TipRegion(rect, BuildListingTooltip(opportunity));

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

                // Leave a gutter so a long value abuts the next column instead of running
                // underneath it. RimWorld clips labels to their rect, so this is what turns
                // an overlapping mess into honest truncation.
                return new Rect(cellX, rect.y + 4f, w[i] - 4f, rect.height - 4f);
            }

            Widgets.Label(Cell(0), opportunity.settlementName);
            Widgets.Label(Cell(1), opportunity.ItemLabel());
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

            Widgets.Label(Cell(7), $"{opportunity.deadlineDays}d");

            // Accept has its own column. Previously it was drawn over the last one, so the
            // deadline text ran underneath the button.
            Rect acceptCell = Cell((int)Column.Accept);
            Rect acceptRect = new Rect(acceptCell.x, rect.y + 3f,
                Mathf.Min(acceptCell.width, 76f), RowHeight - 7f);
            if (Widgets.ButtonText(acceptRect, "Accept"))
            {
                AcceptOpportunity(opportunity);
            }
        }

        /// <summary>
        /// Listing detail (DESIGN.md §101 "unique listing details", "art detail display").
        ///
        /// A furniture or art order is not just a line in a table: it commits the colony to
        /// producing specific objects at a specific standard, and each one travels as its own
        /// crate. The tooltip has to say all of that before the player clicks Accept.
        /// </summary>
        private static string BuildListingTooltip(MarketOpportunity opportunity)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{opportunity.quantity}x {opportunity.ItemLabel()} for {opportunity.settlementName}");
            sb.AppendLine($"Deliver within {opportunity.deadlineDays} days of accepting.");

            if (opportunity.minQuality.HasValue)
            {
                sb.AppendLine($"Only items of {opportunity.minQuality.Value.GetLabel()} quality " +
                              "or better will be accepted.");
            }

            if (opportunity.stuffDef != null)
            {
                sb.AppendLine($"Must be made of {opportunity.stuffDef.label}.");
            }

            if (opportunity.IsCratedGood)
            {
                // Each crated good is one crate with real mass; 8 sculptures is a caravan.
                sb.AppendLine($"Each of the {opportunity.quantity} travels as a separate crate " +
                              "— check your caravan capacity.");
            }

            if (IsArt(opportunity.thingDef))
            {
                sb.AppendLine("Artwork: the buyer values the piece itself. Quality drives the price, " +
                              "and the work keeps its title and author after sale.");
            }

            sb.AppendLine();
            sb.Append(opportunity.priceExplanation);
            return sb.ToString();
        }

        private static bool IsArt(ThingDef def)
        {
            return def != null && def.HasComp(typeof(CompArt));
        }

        /// <summary>
        /// Accepting is a binding commitment, so it is confirmed rather than one misclick away
        /// (§17: do not let a player fail an order they did not understand they had taken on).
        /// </summary>
        private void AcceptOpportunity(MarketOpportunity opportunity)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                return;
            }

            string body =
                $"Deliver {opportunity.quantity}x {opportunity.ItemLabel()} to " +
                $"{opportunity.settlementName} within {opportunity.deadlineDays} days.\n\n" +
                $"Payment: {opportunity.TotalPrice} silver ({opportunity.unitPrice:F2} each)\n" +
                $"Distance: {(opportunity.distanceTiles < 0f ? "unknown" : $"{opportunity.distanceTiles:F0} tiles")}\n\n" +
                "You deliver by taking a caravan to the settlement. Missing the deadline fails the order.";

            Find.WindowStack.Add(new Dialog_MessageBox(
                body,
                "Accept order",
                () =>
                {
                    SalesOrder order = SalesOrderService.Accept(state, opportunity);
                    if (order != null)
                    {
                        tab = Tab.Orders;
                    }
                },
                "Cancel",
                null,
                "Accept this order?"));
        }

        private void DrawOrders(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 34f), "Sales orders");
            y += 38f;
            Text.Font = GameFont.Small;

            List<SalesOrder> orders = new List<SalesOrder>(state.Orders);
            if (orders.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, inRect.width, 60f),
                    "No orders yet. Accept an offer in the Market tab to commit to one.");
                GUI.color = Color.white;
                return;
            }

            // Open orders first, then most recent, so the actionable ones are always on top.
            orders.Sort((a, b) =>
            {
                if (a.IsOpen != b.IsOpen)
                {
                    return a.IsOpen ? -1 : 1;
                }

                return b.id.CompareTo(a.id);
            });

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, orders.Count * OrderRowHeight);

            Widgets.BeginScrollView(outRect, ref ordersScroll, viewRect);
            float rowY = 0f;
            for (int i = 0; i < orders.Count; i++)
            {
                DrawOrderRow(new Rect(0f, rowY, viewRect.width, OrderRowHeight), orders[i], i);
                rowY += OrderRowHeight;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Find Buyer (DESIGN.md §12, §102): "I already have a huge surplus. Who wants it?"
        /// Stock on the left, buyers for the selected good on the right.
        /// </summary>
        private void DrawFindBuyer(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 34f), "Find a buyer");
            y += 38f;
            Text.Font = GameFont.Small;

            Map map = Find.CurrentMap;
            if (map == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, inRect.width, 40f),
                    "Open a colony map to search your stock.");
                GUI.color = Color.white;
                return;
            }

            // Scanning the map is opt-in, never per-frame.
            if (stockCache == null)
            {
                stockCache = FindBuyerService.ColonyStock(map);
            }

            // Sits to the right of the heading, not under it.
            Rect refreshRect = new Rect(inRect.width - 124f, y - 36f, 110f, 26f);
            if (Widgets.ButtonText(refreshRect, "Refresh"))
            {
                stockCache = FindBuyerService.ColonyStock(map);
                findBuyerCache = null;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            TooltipHandler.TipRegion(refreshRect,
                "Re-scan storage. Stock is not tracked live — scanning every frame would cost " +
                "real performance on a large colony.");

            if (stockCache.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, inRect.width, 60f),
                    "Nothing tradeable in storage.\n" +
                    "Stock counts only what is in a stockpile — loose items lying around are not a surplus.");
                GUI.color = Color.white;
                return;
            }

            float listWidth = Mathf.Min(300f, inRect.width * 0.34f);
            Rect stockRect = new Rect(0f, y, listWidth, inRect.yMax - y);
            Rect offersRect = new Rect(listWidth + 12f, y, inRect.width - listWidth - 12f, inRect.yMax - y);

            DrawStockList(stockRect, stockCache);
            DrawBuyerOffers(offersRect, state);
        }

        private void DrawStockList(Rect rect, List<KeyValuePair<ThingDef, int>> stock)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "In storage");
            Rect outRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.height - 26f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, stock.Count * 28f);

            Widgets.BeginScrollView(outRect, ref stockScroll, viewRect);
            float rowY = 0f;
            foreach (KeyValuePair<ThingDef, int> entry in stock)
            {
                Rect row = new Rect(0f, rowY, viewRect.width, 28f);
                if (selectedStockDef == entry.Key)
                {
                    Widgets.DrawHighlightSelected(row);
                }

                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(row.x + 4f, row.y + 3f, row.width - 70f, 24f),
                    entry.Key.LabelCap);
                Widgets.Label(new Rect(row.xMax - 64f, row.y + 3f, 60f, 24f), entry.Value.ToString());

                if (Widgets.ButtonInvisible(row))
                {
                    selectedStockDef = entry.Key;
                    selectedStockCount = entry.Value;

                    // Default to offering everything, which is the common case, but the
                    // player can dial it back below.
                    sellQuantity = entry.Value;
                    findBuyerCache = null;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }

                rowY += 28f;
            }

            Widgets.EndScrollView();
        }

        private void DrawBuyerOffers(Rect rect, IntercolonyWorldComponent state)
        {
            if (selectedStockDef == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rect.x, rect.y + 4f, rect.width, 40f),
                    "Select something from your storage to see who wants it.");
                GUI.color = Color.white;
                return;
            }

            float y = rect.y;

            // Quantity to offer. Changing it re-prices, because a smaller lot avoids
            // saturation and fetches a better unit price (§13) — that trade-off is the whole
            // reason to let the player choose rather than always dumping the whole stockpile.
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
                $"Sell {sellQuantity} of {selectedStockCount} {selectedStockDef.LabelCap}");
            y += 26f;

            sellQuantity = Mathf.Clamp(sellQuantity, 1, Mathf.Max(1, selectedStockCount));
            Rect sliderRect = new Rect(rect.x, y + 4f, rect.width * 0.55f, 20f);
            int newQuantity = Mathf.RoundToInt(Widgets.HorizontalSlider(
                sliderRect, sellQuantity, 1f, Mathf.Max(1, selectedStockCount)));

            Rect allRect = new Rect(sliderRect.xMax + 8f, y, 60f, 26f);
            if (Widgets.ButtonText(allRect, "All"))
            {
                newQuantity = selectedStockCount;
            }

            Rect halfRect = new Rect(allRect.xMax + 4f, y, 60f, 26f);
            if (Widgets.ButtonText(halfRect, "Half"))
            {
                newQuantity = Mathf.Max(1, selectedStockCount / 2);
            }

            if (newQuantity != sellQuantity)
            {
                sellQuantity = newQuantity;
                findBuyerCache = null;
            }

            y += 32f;

            // Searching walks every accessible settlement and prices each one. Cached, and
            // invalidated only when the selection or quantity changes (§84).
            if (findBuyerCache == null)
            {
                findBuyerCache = FindBuyerService.FindBuyers(
                    state, selectedStockDef, null, sellQuantity);
                SortBuyers(findBuyerCache);
            }

            DrawBuyerHeader(new Rect(rect.x, y, rect.width - 16f, 24f));
            y += 24f;
            Widgets.DrawLineHorizontal(rect.x, y, rect.width);
            y += 2f;

            Rect outRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, findBuyerCache.Count * 34f);

            Widgets.BeginScrollView(outRect, ref buyerScroll, viewRect);
            float rowY = 0f;
            for (int i = 0; i < findBuyerCache.Count; i++)
            {
                DrawBuyerRow(new Rect(0f, rowY, viewRect.width, 34f), findBuyerCache[i], i, state);
                rowY += 34f;
            }

            Widgets.EndScrollView();
        }

        private static readonly float[] BuyerColumnWidths = { 0.28f, 0.16f, 0.14f, 0.16f, 0.12f, 0.14f };

        private static readonly string[] BuyerColumnLabels =
            { "Buyer", "Will take", "Unit", "Total", "Dist", "" };

        /// <summary>Sortable headers, matching the Market tab's convention.</summary>
        private void DrawBuyerHeader(Rect rect)
        {
            float x = rect.x;
            for (int i = 0; i < BuyerColumnLabels.Length; i++)
            {
                float w = rect.width * BuyerColumnWidths[i];
                Rect cell = new Rect(x, rect.y, w - 4f, rect.height);

                // Last column holds the Sell button and has nothing to sort on.
                if (i >= BuyerColumnLabels.Length - 1)
                {
                    x += w;
                    continue;
                }

                bool active = (int)buyerSortColumn == i;
                Widgets.DrawHighlightIfMouseover(cell);
                GUI.color = active ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(cell, BuyerColumnLabels[i] + (active ? (buyerSortDescending ? " v" : " ^") : ""));
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(cell))
                {
                    if (active)
                    {
                        buyerSortDescending = !buyerSortDescending;
                    }
                    else
                    {
                        buyerSortColumn = (BuyerColumn)i;
                        // Money and quantity read best biggest-first; names A-Z; distance nearest-first.
                        buyerSortDescending = i != (int)BuyerColumn.Buyer && i != (int)BuyerColumn.Distance;
                    }

                    SortBuyers(findBuyerCache);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }

                x += w;
            }
        }

        private void SortBuyers(List<BuyerOffer> offers)
        {
            if (offers == null)
            {
                return;
            }

            Comparison<BuyerOffer> comparison;
            switch (buyerSortColumn)
            {
                case BuyerColumn.Buyer:
                    comparison = (a, b) => string.Compare(
                        a.settlement?.Label ?? "", b.settlement?.Label ?? "",
                        StringComparison.CurrentCultureIgnoreCase);
                    break;
                case BuyerColumn.MaxQuantity:
                    comparison = (a, b) => a.maxQuantity.CompareTo(b.maxQuantity);
                    break;
                case BuyerColumn.UnitPrice:
                    comparison = (a, b) => a.unitPrice.CompareTo(b.unitPrice);
                    break;
                case BuyerColumn.Distance:
                    comparison = (a, b) => SortableOfferDistance(a).CompareTo(SortableOfferDistance(b));
                    break;
                default:
                    comparison = (a, b) => a.TotalPrice.CompareTo(b.TotalPrice);
                    break;
            }

            offers.Sort((a, b) =>
            {
                // Uninterested settlements have no numbers to compare and always sort last,
                // whatever the column — otherwise reversing a sort buries every real offer.
                if (a.Interested != b.Interested)
                {
                    return a.Interested ? -1 : 1;
                }

                if (!a.Interested)
                {
                    return string.Compare(a.settlement?.Label ?? "", b.settlement?.Label ?? "",
                        StringComparison.CurrentCultureIgnoreCase);
                }

                int result = comparison(a, b);
                if (result != 0)
                {
                    return buyerSortDescending ? -result : result;
                }

                return (a.settlement?.ID ?? 0).CompareTo(b.settlement?.ID ?? 0);
            });
        }

        private static float SortableOfferDistance(BuyerOffer offer)
        {
            return offer.distanceTiles < 0f ? float.MaxValue : offer.distanceTiles;
        }

        private void DrawBuyerRow(Rect rect, BuyerOffer offer, int index, IntercolonyWorldComponent state)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);

            Widgets.Label(new Rect(rect.x + 4f, rect.y + 6f, rect.width * 0.28f, 24f),
                offer.settlement?.Label ?? "?");

            if (!offer.Interested)
            {
                // §12 shows uninterested settlements explicitly — "nobody nearby wants this"
                // is a useful answer, and hiding it looks like a broken search.
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rect.x + rect.width * 0.30f, rect.y + 6f, rect.width * 0.5f, 24f),
                    offer.noInterestReason);
                GUI.color = Color.white;
                return;
            }

            // Columns must line up with DrawBuyerHeader or sorting looks arbitrary.
            float x = rect.x + rect.width * BuyerColumnWidths[0];
            // Total appetite, not the amount currently offered: the useful question is "how
            // much would this settlement absorb before losing interest", which is what lets
            // the player split a surplus across several buyers.
            Widgets.Label(new Rect(x, rect.y + 6f, rect.width * BuyerColumnWidths[1] - 4f, 24f),
                offer.maxQuantity.ToString());
            x += rect.width * BuyerColumnWidths[1];

            Widgets.Label(new Rect(x, rect.y + 6f, rect.width * BuyerColumnWidths[2] - 4f, 24f),
                $"{offer.unitPrice:F2}");
            x += rect.width * BuyerColumnWidths[2];

            Widgets.Label(new Rect(x, rect.y + 6f, rect.width * BuyerColumnWidths[3] - 4f, 24f),
                offer.TotalPrice.ToString());
            x += rect.width * BuyerColumnWidths[3];

            Widgets.Label(new Rect(x, rect.y + 6f, rect.width * BuyerColumnWidths[4] - 4f, 24f),
                offer.distanceTiles < 0f ? "?" : $"{offer.distanceTiles:F0} t");

            TooltipHandler.TipRegion(rect,
                $"{offer.settlement?.Label} would take up to {offer.maxQuantity} " +
                $"{selectedStockDef?.label}.\n" +
                $"Selling {offer.quantity} at {offer.unitPrice:F2} each = {offer.TotalPrice} silver.\n\n" +
                IntercolonyPricing.Explain(offer.def, offer.stuff, offer.quantity, offer.unitPrice, offer.factors));

            Rect sellRect = new Rect(rect.xMax - 84f, rect.y + 4f, 78f, 26f);
            if (Widgets.ButtonText(sellRect, "Sell"))
            {
                ConfirmSell(state, offer);
            }
        }

        private void ConfirmSell(IntercolonyWorldComponent state, BuyerOffer offer)
        {
            const int DeadlineDays = 12;

            string body =
                $"Commit to delivering {offer.quantity}x {offer.def.LabelCap} to " +
                $"{offer.settlement?.Label} within {DeadlineDays} days.\n\n" +
                $"Payment: {offer.TotalPrice} silver ({offer.unitPrice:F2} each)\n" +
                $"Distance: {(offer.distanceTiles < 0f ? "unknown" : $"{offer.distanceTiles:F0} tiles")}\n\n" +
                "This is a binding order. Your stock is not reserved — anything the colony " +
                "consumes in the meantime still has to be replaced before the deadline.";

            Find.WindowStack.Add(new Dialog_MessageBox(
                body,
                "Create order",
                () =>
                {
                    if (SalesOrderService.CreateFromOffer(state, offer, offer.quantity, DeadlineDays) != null)
                    {
                        tab = Tab.Orders;
                        findBuyerCache = null;
                    }
                },
                "Cancel",
                null,
                "Sell to this buyer?"));
        }

        private const float OrderRowHeight = 56f;

        private void DrawOrderRow(Rect rect, SalesOrder order, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);

            Rect main = new Rect(rect.x + 4f, rect.y + 3f, rect.width - 200f, rect.height - 6f);
            string title = $"#{order.id}  {order.settlementName} — {order.Quantity}x " +
                           $"{order.line?.ShortLabel() ?? "<missing>"}";

            Widgets.Label(new Rect(main.x, main.y, main.width, 22f), title);

            // §17: show progress and time remaining, and warn rather than fail silently.
            string detail;
            Color colour = Color.white;
            if (order.IsOpen)
            {
                detail = $"{order.deliveredQuantity}/{order.Quantity} delivered   " +
                         $"{order.DaysRemaining:F1}d left   " +
                         $"{order.TotalPayment} silver on completion";
                if (order.DaysRemaining < 1f)
                {
                    colour = Color.yellow;
                }
            }
            else
            {
                detail = $"{order.status}: {order.outcomeNote}";
                colour = order.status == SalesOrderStatus.Completed
                    ? new Color(0.6f, 0.9f, 0.6f)
                    : new Color(0.9f, 0.6f, 0.6f);
            }

            GUI.color = colour;
            Widgets.Label(new Rect(main.x, main.y + 24f, main.width, 22f), detail);
            GUI.color = Color.white;

            if (!order.IsOpen)
            {
                return;
            }

            Rect cancelRect = new Rect(rect.xMax - 90f, rect.y + 14f, 80f, 28f);
            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Cancel order #{order.id} for {order.settlementName}? " +
                    "You will not be paid for anything already delivered.",
                    () => SalesOrderService.Cancel(order),
                    destructive: true));
            }
        }
    }
}
