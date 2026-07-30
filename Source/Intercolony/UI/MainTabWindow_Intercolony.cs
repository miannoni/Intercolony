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
    public partial class MainTabWindow_Intercolony : MainTabWindow
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

        // Seven tabs, and the Labor tab shows two tables stacked. 920x560 left both cramped.
        public override Vector2 RequestedTabSize => new Vector2(1040f, 620f);

        /// <summary>Which top-level view is showing (DESIGN.md §52, §53, §54).</summary>
        private enum Tab
        {
            Market,
            Orders,
            FindBuyer,
            Procurement,
            Labor,
            Contracts,
            Relations
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

            if (tab == Tab.Procurement)
            {
                DrawProcurement(body, state);
                return;
            }

            if (tab == Tab.Contracts)
            {
                DrawContracts(body, state);
                return;
            }

            if (tab == Tab.Labor)
            {
                DrawLabor(body, state);
                return;
            }

            if (tab == Tab.Relations)
            {
                DrawRelations(body, state);
                return;
            }

            DrawMarket(body, state);
        }

        /// <summary>
        /// Tab order, left to right. Adding a tab means adding it here and nowhere else.
        ///
        /// This used to be seven hand-computed <see cref="Rect"/>s at a fixed 150px, positioned
        /// relative to each other in a different order than they appeared on screen. Six tabs at
        /// 150px plus gaps already exceeded the window width, so Labor could not be added without
        /// overflowing, and the arithmetic was one edit away from overlapping buttons.
        /// </summary>
        private static readonly Tab[] TabOrder =
        {
            Tab.Market,
            Tab.Orders,
            Tab.FindBuyer,
            Tab.Procurement,
            Tab.Labor,
            Tab.Contracts,
            Tab.Relations
        };

        /// <summary>Tab caption, including a count badge where one is useful.</summary>
        private static string TabLabel(Tab which, IntercolonyWorldComponent state)
        {
            switch (which)
            {
                case Tab.Orders:
                    int orders = state.OpenOrderCount;
                    return orders > 0 ? $"Orders ({orders})" : "Orders";
                case Tab.FindBuyer:
                    return "Find buyer";
                case Tab.Procurement:
                    int requests = state.OpenRequestCount;
                    return requests > 0 ? $"Procurement ({requests})" : "Procurement";
                case Tab.Labor:
                    // An unpaid-wages badge on the tab itself, because §39's escalation is only
                    // playable if the player notices it without going looking.
                    if (PayrollService.TotalOwed(state) > 0)
                    {
                        return "Labor (!)";
                    }

                    int employees = state.ActiveEmployeeCount;
                    return employees > 0 ? $"Labor ({employees})" : "Labor";
                case Tab.Contracts:
                    int contracts = state.ActiveContractCount;
                    return contracts > 0 ? $"Contracts ({contracts})" : "Contracts";
                default:
                    return which.ToString();
            }
        }

        private float DrawTabSelector(Rect inRect, IntercolonyWorldComponent state)
        {
            const float ButtonHeight = 30f;
            const float Gap = 6f;
            const float MaxButtonWidth = 150f;

            float available = inRect.width - Gap * (TabOrder.Length - 1);
            float buttonWidth = Mathf.Min(MaxButtonWidth, available / TabOrder.Length);

            float x = 0f;
            foreach (Tab which in TabOrder)
            {
                Rect rect = new Rect(x, 0f, buttonWidth, ButtonHeight);
                if (Widgets.ButtonText(rect, TabLabel(which, state), drawBackground: tab != which))
                {
                    SelectTab(which, state);
                }

                x += buttonWidth + Gap;
            }

            return ButtonHeight + 8f;
        }

        private void SelectTab(Tab which, IntercolonyWorldComponent state)
        {
            tab = which;

            if (which == Tab.FindBuyer)
            {
                // Re-scan once on entry so the list is current without being live.
                stockCache = null;
                findBuyerCache = null;
            }
            else if (which == Tab.Labor)
            {
                // Cheap: the pool is cached per market refresh and only built when stale.
                LaborCandidateService.Refresh(state);
            }
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

            GUI.color = opportunity.fulfillment == FulfillmentMode.BuyerPickup
                ? new Color(0.6f, 0.85f, 1f)
                : Color.white;
            Widgets.Label(Cell(7), opportunity.fulfillment == FulfillmentMode.BuyerPickup
                ? "collected"
                : $"{opportunity.deadlineDays}d haul");
            GUI.color = Color.white;

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

            // §105: the mode is half the decision, so it belongs in the listing detail.
            sb.AppendLine(opportunity.fulfillment == FulfillmentMode.BuyerPickup
                ? "The buyer collects: no caravan needed, but they pay less for handling it."
                : "You deliver: a caravan trip, paid at a premium for taking it on.");

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

        /// <summary>
        /// Unit rate a buyer pays for this lot size. Re-computed rather than reused, so the
        /// confirmation slider moves the per-unit price the way saturation says it should (§13).
        /// </summary>
        private static float SellRateFor(BuyerOffer offer, int quantity)
        {
            if (offer?.def == null || offer.profile == null)
            {
                return offer?.unitPrice ?? 0f;
            }

            IntercolonyProductCategory category =
                IntercolonyProductClassifier.Classify(offer.def) ?? IntercolonyProductCategory.Commodities;

            return IntercolonyPricing.UnitPrice(
                offer.def, offer.stuff, Mathf.Max(1, quantity), offer.profile,
                category, offer.distanceTiles, null, out _);
        }

        /// <summary>Profile for a settlement id, or null if it is gone. Used for live re-pricing.</summary>
        private static SettlementEconomicProfile ProfileFor(IntercolonyWorldComponent state, int settlementId)
        {
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(settlementId);
            return settlement == null ? null : state.GetProfile(settlement);
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

            Find.WindowStack.Add(new Dialog_ConfirmQuantity(
                "Accept this order?",
                "Accept order",
                opportunity.quantity,
                qty =>
                {
                    string logistics = opportunity.fulfillment == FulfillmentMode.BuyerPickup
                        ? $"{opportunity.settlementName} will collect from your storage once you " +
                          "declare the goods ready. No caravan needed."
                        : "You deliver by caravan. Missing the deadline fails the order.";

                    string partial = qty < opportunity.quantity
                        ? $"\n\nThe buyer asked for {opportunity.quantity}. Committing to {qty} is a " +
                          "smaller deal, not a partial one — you owe exactly what you accept.\n" +
                          "A smaller lot earns a better rate per unit."
                        : "";

                    float rate = IntercolonyPricing.RepriceForQuantity(
                        opportunity, ProfileFor(state, opportunity.settlementId), qty, out _);

                    return $"Supply {qty}x {opportunity.ItemLabel()} to {opportunity.settlementName} " +
                           $"within {opportunity.deadlineDays} days.\n\n" +
                           $"Payment: {Mathf.RoundToInt(rate * qty)} silver ({rate:F2} each)\n" +
                           $"Distance: {(opportunity.distanceTiles < 0f ? "unknown" : $"{opportunity.distanceTiles:F0} tiles")}\n\n" +
                           logistics + partial;
                },
                qty =>
                {
                    SalesOrder order = SalesOrderService.Accept(state, opportunity, qty);
                    if (order != null)
                    {
                        tab = Tab.Orders;
                    }
                }));
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

            // The search runs against the full stock; choosing how much to actually sell
            // happens in the confirmation dialog, where every other commitment is made.
            sellQuantity = Mathf.Max(1, selectedStockCount);

            Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
                $"Buyers for {selectedStockCount}x {selectedStockDef.LabelCap}");
            y += 28f;

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

            Find.WindowStack.Add(new Dialog_ConfirmQuantity(
                "Sell to this buyer?",
                "Create order",
                offer.quantity,
                qty =>
                {
                    float rate = SellRateFor(offer, qty);
                    return $"Commit to delivering {qty}x {offer.def.LabelCap} to " +
                           $"{offer.settlement?.Label} within {DeadlineDays} days.\n\n" +
                           $"Payment: {Mathf.RoundToInt(rate * qty)} silver ({rate:F2} each)\n" +
                           $"Distance: {(offer.distanceTiles < 0f ? "unknown" : $"{offer.distanceTiles:F0} tiles")}\n\n" +
                           "This is a binding order. Your stock is not reserved — anything the colony " +
                           "consumes in the meantime still has to be replaced before the deadline." +
                           (qty < offer.quantity ? "\n\nA smaller lot earns a better rate per unit." : "");
                },
                qty =>
                {
                    BuyerOffer priced = offer;
                    priced.unitPrice = SellRateFor(offer, qty);
                    if (SalesOrderService.CreateFromOffer(state, priced, qty, DeadlineDays) != null)
                    {
                        tab = Tab.Orders;
                        findBuyerCache = null;
                    }
                }));
        }

        private Vector2 procurementScroll;

        /// <summary>
        /// Procurement (DESIGN.md §19, §55, §103). Requests with their quotes underneath, so
        /// comparing suppliers is a matter of reading down a list rather than clicking through.
        /// </summary>
        private void DrawProcurement(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width - 200f, 34f), "Procurement");
            Text.Font = GameFont.Small;

            Rect newRect = new Rect(inRect.width - 190f, y + 2f, 180f, 30f);
            if (Widgets.ButtonText(newRect, "Request goods..."))
            {
                Find.WindowStack.Add(new Dialog_CreateRequest(state));
            }

            y += 40f;

            y = DrawPurchaseOrders(inRect, y, state);

            List<PurchaseRequest> requests = new List<PurchaseRequest>(state.Requests);
            if (requests.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, inRect.width, 70f),
                    "No requests yet.\n\n" +
                    "Intercolony is not a shop. You state what you need, and known settlements " +
                    "answer if they can — sometimes with less than you asked for, sometimes not at all.");
                GUI.color = Color.white;
                return;
            }

            // Open requests first, newest first within each group.
            requests.Sort((a, b) =>
            {
                if (a.IsOpen != b.IsOpen)
                {
                    return a.IsOpen ? -1 : 1;
                }

                return b.id.CompareTo(a.id);
            });

            float contentHeight = 0f;
            foreach (PurchaseRequest request in requests)
            {
                contentHeight += RequestBlockHeight(request);
            }

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(outRect, ref procurementScroll, viewRect);
            float rowY = 0f;
            foreach (PurchaseRequest request in requests)
            {
                float height = RequestBlockHeight(request);
                DrawRequestBlock(new Rect(0f, rowY, viewRect.width, height), request, state);
                rowY += height;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Live purchases, above the requests. These are money already spent, so they are the
        /// first thing the player should see on this tab.
        /// </summary>
        private float DrawPurchaseOrders(Rect inRect, float y, IntercolonyWorldComponent state)
        {
            List<PurchaseOrder> open = new List<PurchaseOrder>();
            foreach (PurchaseOrder order in state.PurchaseOrders)
            {
                if (order.IsOpen)
                {
                    open.Add(order);
                }
            }

            if (open.Count == 0)
            {
                return y;
            }

            Widgets.Label(new Rect(0f, y, inRect.width, 24f), $"On order ({open.Count})");
            y += 26f;

            foreach (PurchaseOrder order in open)
            {
                Rect row = new Rect(0f, y, inRect.width - 16f, 26f);
                Widgets.DrawLightHighlight(row);
                Widgets.DrawHighlightIfMouseover(row);

                Widgets.Label(new Rect(row.x + 6f, row.y + 2f, row.width * 0.42f, 22f),
                    $"#{order.id}  {order.quantity}x {order.ItemLabel()}");
                Widgets.Label(new Rect(row.x + row.width * 0.44f, row.y + 2f, row.width * 0.22f, 22f),
                    order.settlementName);

                string statusText;
                Color colour = Color.white;
                if (order.status == PurchaseOrderStatus.ReadyForPickup)
                {
                    statusText = $"collect within {order.DaysUntilPickupExpires:F1}d";
                    colour = new Color(0.6f, 0.9f, 0.6f);
                }
                else
                {
                    statusText = order.supplierDelivers
                        ? $"arriving in {order.DaysUntilReady:F1}d"
                        : $"ready in {order.DaysUntilReady:F1}d";
                }

                GUI.color = colour;
                Widgets.Label(new Rect(row.x + row.width * 0.66f, row.y + 2f, row.width * 0.26f, 22f),
                    statusText);
                GUI.color = Color.white;

                TooltipHandler.TipRegion(row,
                    $"{order.quantity}x {order.ItemLabel()} from {order.settlementName}\n" +
                    $"Paid {order.paidSilver} silver.\n" +
                    (order.supplierDelivers
                        ? "They deliver to your colony."
                        : "Send a caravan to collect. Use the caravan's Collect button at the settlement."));

                y += 26f;
            }

            return y + 8f;
        }

        private Vector2 contractsScroll;

        /// <summary>
        /// Recurring contracts (DESIGN.md §29, §107). Offers first, then live agreements, then
        /// history — a proposal on the table is the only thing here needing a decision.
        /// </summary>
        private void DrawContracts(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 34f), "Supply agreements");
            y += 38f;
            Text.Font = GameFont.Small;

            List<RecurringContract> contracts = new List<RecurringContract>(state.Contracts);
            if (contracts.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, inRect.width, 80f),
                    "No supply agreements.\n\n" +
                    "Settlements that trust you may offer standing agreements — a fixed quantity " +
                    "every quadrum for a fixed term, at better than spot prices. Build a trading " +
                    "record first: a settlement will not stake a year of supply on a stranger.");
                GUI.color = Color.white;
                return;
            }

            contracts.Sort((a, b) =>
            {
                int rank = ContractRank(a).CompareTo(ContractRank(b));
                return rank != 0 ? rank : b.id.CompareTo(a.id);
            });

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contracts.Count * 74f);

            Widgets.BeginScrollView(outRect, ref contractsScroll, viewRect);
            float rowY = 0f;
            for (int i = 0; i < contracts.Count; i++)
            {
                DrawContractRow(new Rect(0f, rowY, viewRect.width, 74f), contracts[i], i, state);
                rowY += 74f;
            }

            Widgets.EndScrollView();
        }

        private static int ContractRank(RecurringContract contract)
        {
            if (contract.IsOffer) return 0;
            if (contract.IsActive) return 1;

            // Suspended sorts with the live agreements, not with the dead ones. It is still an
            // obligation the player has to plan around (§88), and burying it below cancelled
            // contracts would suggest otherwise.
            if (contract.status == ContractStatus.Suspended) return 2;
            return 3;
        }

        private void DrawContractRow(
            Rect rect, RecurringContract contract, int index, IntercolonyWorldComponent state)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);

            Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 220f, 22f),
                $"#{contract.id}  {contract.settlementName} — {contract.quantityPerCycle}x " +
                $"{contract.ItemLabel()} every {contract.CadenceDays:F0}d");

            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 26f, rect.width - 220f, 22f),
                $"{contract.totalCycles} deliveries   {contract.CycleValue} silver each   " +
                $"{contract.TotalValue} total");
            GUI.color = Color.white;

            string status;
            Color colour = Color.white;
            if (contract.IsOffer)
            {
                status = $"offer expires in {contract.DaysUntilOfferExpires:F1}d";
                colour = new Color(0.6f, 0.9f, 1f);
            }
            else if (contract.IsActive)
            {
                status = contract.activeOrderId != 0
                    ? $"delivery {contract.cyclesCompleted + contract.cyclesFailed + 1} in progress"
                    : $"next delivery in {contract.DaysUntilNextCycle:F1}d";
                if (contract.consecutiveFailures > 0)
                {
                    status += "  — one more miss ends it";
                    colour = Color.yellow;
                }
            }
            else if (contract.status == ContractStatus.Suspended)
            {
                // Amber, not red. §88's suspension is not a failure and the colour has to say so —
                // the agreement is intact and the remaining deliveries are still owed to the player.
                status = $"suspended by war with {contract.factionName} — " +
                         $"{contract.CyclesRemaining} deliveries still to come";
                colour = new Color(1f, 0.8f, 0.4f);
            }
            else
            {
                status = $"{contract.status}: {contract.outcomeNote}";
                colour = contract.status == ContractStatus.Completed
                    ? new Color(0.6f, 0.9f, 0.6f)
                    : new Color(0.9f, 0.6f, 0.6f);
            }

            GUI.color = colour;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 48f, rect.width - 220f, 22f),
                $"{contract.cyclesCompleted} delivered, {contract.cyclesFailed} missed — {status}");
            GUI.color = Color.white;

            TooltipHandler.TipRegion(rect,
                $"{contract.settlementName} ({contract.factionName})\n\n" +
                $"{contract.quantityPerCycle}x {contract.ItemLabel()} every " +
                $"{contract.CadenceDays:F0} days, {contract.totalCycles} times.\n" +
                $"{contract.unitPrice:F2} silver each — above spot, because they are buying certainty.\n\n" +
                "Each cycle raises a delivery order with the full cadence as its deadline. " +
                $"Missing {RecurringContract.BreachThreshold} deliveries in a row ends the " +
                "agreement and badly damages your standing.");

            if (contract.IsOffer)
            {
                Rect acceptRect = new Rect(rect.xMax - 200f, rect.y + 20f, 92f, 30f);
                if (Widgets.ButtonText(acceptRect, "Accept"))
                {
                    ConfirmContract(state, contract);
                }

                Rect declineRect = new Rect(rect.xMax - 100f, rect.y + 20f, 92f, 30f);
                if (Widgets.ButtonText(declineRect, "Decline"))
                {
                    contract.TryDecline();
                }
            }
            else if (contract.IsActive || contract.status == ContractStatus.Suspended)
            {
                // Withdrawing from a suspended agreement is allowed: the player may not want to be
                // held to eight quadrums of rice for a faction they are now at war with, and
                // forcing them to wait for peace to say so would be a worse kind of limbo.
                bool suspended = contract.status == ContractStatus.Suspended;

                Rect cancelRect = new Rect(rect.xMax - 100f, rect.y + 20f, 92f, 30f);
                if (Widgets.ButtonText(cancelRect, "Withdraw"))
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Withdraw from the agreement with {contract.settlementName}?\n\n" +
                        (suspended
                            ? "It is only suspended — it would resume on its own if relations " +
                              "recovered. Withdrawing ends it for good."
                            : "They will think considerably less of you as a supplier."),
                        () => ContractService.CancelContract(state, contract),
                        destructive: true));
                }
            }
        }

        private void ConfirmContract(IntercolonyWorldComponent state, RecurringContract contract)
        {
            // §29's objective is that a commitment drives production planning, so the
            // confirmation states the ongoing obligation in those terms rather than just a price.
            string body =
                $"{contract.settlementName} wants {contract.quantityPerCycle}x " +
                $"{contract.ItemLabel()} every {contract.CadenceDays:F0} days, " +
                $"{contract.totalCycles} times.\n\n" +
                $"Payment: {contract.CycleValue} silver per delivery, {contract.TotalValue} in total\n" +
                $"Rate: {contract.unitPrice:F2} each — better than spot, because they are buying certainty\n\n" +
                $"That is roughly {contract.quantityPerCycle / Mathf.Max(1f, contract.CadenceDays):F1} " +
                "units per day of sustained output. Make sure you can hold that pace: missing " +
                $"{RecurringContract.BreachThreshold} deliveries in a row ends the agreement and " +
                "badly damages your standing with them.";

            Find.WindowStack.Add(new Dialog_MessageBox(
                body,
                "Accept agreement",
                () => ContractService.AcceptOffer(state, contract),
                "Decline",
                () => contract.TryDecline(),
                "Standing supply agreement"));
        }

        private Vector2 relationsScroll;

        /// <summary>
        /// Relationship view (DESIGN.md §57, §27). Shows commercial reputation alongside
        /// faction goodwill, because §27's whole point is that they are different things:
        /// goodwill is whether they shoot at you, reputation is whether they rely on you.
        /// </summary>
        private void DrawRelations(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 34f), "Trading relationships");
            y += 38f;
            Text.Font = GameFont.Small;

            List<CommercialReputation> records = new List<CommercialReputation>();
            foreach (KeyValuePair<int, CommercialReputation> entry in state.Reputations)
            {
                records.Add(entry.Value);
            }

            if (records.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, inRect.width, 60f),
                    "No trading history yet.\n\n" +
                    "Complete or fail an order and that settlement will form an opinion. " +
                    "Reputation is held per settlement and is separate from faction goodwill — " +
                    "being liked is not the same as being relied on.");
                GUI.color = Color.white;
                return;
            }

            records.Sort((a, b) => b.Score.CompareTo(a.Score));

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, records.Count * 58f);

            Widgets.BeginScrollView(outRect, ref relationsScroll, viewRect);
            float rowY = 0f;
            for (int i = 0; i < records.Count; i++)
            {
                DrawRelationRow(new Rect(0f, rowY, viewRect.width, 58f), records[i], i);
                rowY += 58f;
            }

            Widgets.EndScrollView();
        }

        private void DrawRelationRow(Rect rect, CommercialReputation rep, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);

            Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width * 0.34f, 24f),
                $"{rep.settlementName}");

            GUI.color = TierColour(rep.Tier);
            Widgets.Label(new Rect(rect.x + rect.width * 0.36f, rect.y + 4f, rect.width * 0.28f, 24f),
                $"{rep.ScoreDisplay}/100  {rep.TierLabel()}");
            GUI.color = Color.white;

            // Owning faction and its goodwill beside it, per §27's illustrative UI — the
            // two numbers together are the point: liked is not the same as relied upon.
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(rep.settlementId);
            Faction faction = settlement?.Faction;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(rect.x + rect.width * 0.66f, rect.y + 4f, rect.width * 0.34f, 24f),
                faction != null
                    ? $"{rep.factionName}  goodwill {faction.PlayerGoodwill:+#;-#;0}"
                    : $"{rep.factionName}  (gone)");
            GUI.color = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 28f, rect.width - 12f, 24f),
                $"{rep.ordersCompleted} completed   {rep.ordersLate} late   " +
                $"{rep.ordersFailed} failed   {rep.ordersCancelled} cancelled   " +
                $"{rep.purchasesCompleted} purchases");
            GUI.color = Color.white;

            TooltipHandler.TipRegion(rect,
                $"{rep.factionName}\n" +
                $"Commercial reputation: {rep.ScoreDisplay}/100 ({rep.TierLabel()})\n\n" +
                "A better record means larger orders, more frequent offers, slightly better " +
                "prices and more generous deadlines.\n\n" +
                "This is separate from faction goodwill, and it is held by this settlement " +
                "rather than its faction: another town of the same faction forms its own view.");
        }

        private static Color TierColour(ReputationTier tier)
        {
            switch (tier)
            {
                case ReputationTier.Untrusted: return new Color(0.9f, 0.5f, 0.5f);
                case ReputationTier.Unproven: return new Color(0.9f, 0.8f, 0.6f);
                case ReputationTier.Reliable: return new Color(0.7f, 0.9f, 0.7f);
                case ReputationTier.Preferred: return new Color(0.6f, 0.9f, 1f);
                default: return Color.white;
            }
        }

        private const float RequestHeaderHeight = 46f;
        private const float QuoteRowHeight = 26f;

        private static float RequestBlockHeight(PurchaseRequest request)
        {
            int rows = Mathf.Max(1, request.quotes.Count);
            return RequestHeaderHeight + rows * QuoteRowHeight + 10f;
        }

        private void DrawRequestBlock(Rect rect, PurchaseRequest request, IntercolonyWorldComponent state)
        {
            Widgets.DrawLightHighlight(new Rect(rect.x, rect.y, rect.width, RequestHeaderHeight));

            string header = $"#{request.id}  {request.quantityRequested}x {request.ItemLabel()}";
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 200f, 22f), header);

            string sub;
            Color colour = Color.white;
            if (request.IsOpen)
            {
                sub = request.AnyQuotes
                    ? $"{request.quotes.Count} quote(s) — offers stand for {request.DaysRemaining:F1}d"
                    : $"No supplier answered: {request.noResponseReason}";
                if (!request.AnyQuotes)
                {
                    colour = new Color(0.9f, 0.7f, 0.5f);
                }
            }
            else
            {
                sub = request.status.ToString();
                colour = new Color(0.7f, 0.7f, 0.7f);
            }

            GUI.color = colour;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 24f, rect.width - 200f, 22f), sub);
            GUI.color = Color.white;

            if (request.IsOpen)
            {
                Rect cancelRect = new Rect(rect.xMax - 96f, rect.y + 10f, 86f, 26f);
                if (Widgets.ButtonText(cancelRect, "Withdraw"))
                {
                    request.TryCancel();
                }
            }

            float rowY = rect.y + RequestHeaderHeight;
            if (request.quotes.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Widgets.Label(new Rect(rect.x + 20f, rowY + 2f, rect.width - 40f, 22f),
                    "— nothing available —");
                GUI.color = Color.white;
                return;
            }

            foreach (Quotation quote in request.quotes)
            {
                DrawQuoteRow(new Rect(rect.x + 16f, rowY, rect.width - 24f, QuoteRowHeight),
                    quote, request);
                rowY += QuoteRowHeight;
            }
        }

        private void DrawQuoteRow(Rect rect, Quotation quote, PurchaseRequest request)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            bool partial = quote.quantityOffered < request.quantityRequested;

            Widgets.Label(new Rect(rect.x, rect.y + 2f, rect.width * 0.26f, 22f), quote.settlementName);

            // A partial quote is flagged, not hidden: §20 makes partial answers a first-class
            // outcome, and combining two suppliers is a legitimate move.
            GUI.color = partial ? new Color(0.9f, 0.8f, 0.5f) : Color.white;
            Widgets.Label(new Rect(rect.x + rect.width * 0.26f, rect.y + 2f, rect.width * 0.16f, 22f),
                partial
                    ? $"{quote.quantityOffered} of {request.quantityRequested}"
                    : $"{quote.quantityOffered}");
            GUI.color = Color.white;

            Widgets.Label(new Rect(rect.x + rect.width * 0.42f, rect.y + 2f, rect.width * 0.14f, 22f),
                $"{quote.unitPrice:F2}");
            Widgets.Label(new Rect(rect.x + rect.width * 0.56f, rect.y + 2f, rect.width * 0.14f, 22f),
                quote.TotalPrice.ToString());
            Widgets.Label(new Rect(rect.x + rect.width * 0.70f, rect.y + 2f, rect.width * 0.16f, 22f),
                $"{quote.FulfillmentLabel}, {quote.leadTimeDays}d");
            if (request.IsOpen)
            {
                Rect buyRect = new Rect(rect.xMax - 66f, rect.y + 1f, 62f, 24f);
                if (Widgets.ButtonText(buyRect, "Buy"))
                {
                    ConfirmPurchase(request, quote);
                }
            }
            else
            {
                Widgets.Label(new Rect(rect.x + rect.width * 0.86f, rect.y + 2f, rect.width * 0.14f, 22f),
                    quote.distanceTiles < 0f ? "?" : $"{quote.distanceTiles:F0} t");
            }

            TooltipHandler.TipRegion(rect,
                $"{quote.settlementName} ({quote.factionName})\n" +
                $"{quote.quantityOffered} of {request.quantityRequested} requested\n" +
                (quote.offeredQuality.HasValue ? $"Quality: {quote.offeredQuality.Value.GetLabel()}\n" : "") +
                (quote.offeredStuff != null ? $"Material: {quote.offeredStuff.label}\n" : "") +
                $"{(quote.supplierDelivers ? "They deliver it" : "You collect it")}, " +
                $"ready in {quote.leadTimeDays} days\n\n" +
                quote.priceExplanation);
        }

        /// <summary>
        /// Buying spends real silver up front, so it is confirmed and the confirmation states
        /// exactly what was promised — §104's criterion is that goods "preserve expected
        /// properties", and the player has to know what those were to notice if they do not.
        /// </summary>
        private void ConfirmPurchase(PurchaseRequest request, Quotation quote)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (state == null || map == null)
            {
                return;
            }

            int silver = PurchaseOrderService.CountColonySilver(map);

            Find.WindowStack.Add(new Dialog_ConfirmQuantity(
                "Confirm purchase?",
                "Buy",
                quote.quantityOffered,
                qty =>
                {
                    int cost = Mathf.RoundToInt(quote.unitPrice * qty);

                    StringBuilder body = new StringBuilder();
                    body.AppendLine($"Buy {qty}x {request.thingDef?.LabelCap} from {quote.settlementName}.");
                    body.AppendLine();
                    if (quote.offeredQuality.HasValue)
                    {
                        body.AppendLine($"Quality: {quote.offeredQuality.Value.GetLabel()}");
                    }

                    if (quote.offeredStuff != null)
                    {
                        body.AppendLine($"Material: {quote.offeredStuff.label}");
                    }

                    body.AppendLine($"Price: {cost} silver ({quote.unitPrice:F2} each)");
                    body.AppendLine(quote.supplierDelivers
                        ? $"They will deliver to your colony in {quote.leadTimeDays} days."
                        : $"Ready to collect at {quote.settlementName} in {quote.leadTimeDays} days. " +
                          "Send a caravan — goods left uncollected are eventually resold.");
                    body.AppendLine();
                    body.Append("Payment is taken now. Cancelling later forfeits it.");

                    // Shown rather than blocking the dialog: the player can dial the quantity
                    // down until it is affordable, which is exactly why the slider is here.
                    if (cost > silver)
                    {
                        body.Append($"\n\nNot enough silver — short by {cost - silver}. " +
                                    "Reduce the quantity or come back richer.");
                    }

                    return body.ToString();
                },
                qty => PurchaseOrderService.AcceptQuote(state, request, quote, map, qty)));
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
            if (order.BuyerEnRoute)
            {
                detail = $"{order.settlementName} arriving in {order.DaysUntilBuyerArrives:F1}d " +
                         $"to collect {order.RemainingQuantity} — keep them in storage";
                colour = new Color(0.6f, 0.85f, 1f);
            }
            else if (order.IsOpen)
            {
                string mode = order.fulfillment == FulfillmentMode.BuyerPickup
                    ? "buyer collects"
                    : "you deliver";
                detail = $"{order.deliveredQuantity}/{order.Quantity} delivered   " +
                         $"{order.DaysRemaining:F1}d left   " +
                         $"{order.TotalPayment} silver   ({mode})";
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

            // Buyer pickup: the player declares the goods ready and the buyer travels (§25.2).
            if (order.CanMarkReady)
            {
                Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                int have = OrderValidator.CountMatchingInColony(order, map);
                bool enough = have >= order.RemainingQuantity;

                Rect readyRect = new Rect(rect.xMax - 210f, rect.y + 14f, 110f, 28f);
                if (Widgets.ButtonText(readyRect, "Mark ready", active: enough))
                {
                    SalesOrderService.MarkReadyForPickup(order, map);
                }

                TooltipHandler.TipRegion(readyRect, enough
                    ? $"Tell {order.settlementName} the goods are ready. Their caravan will " +
                      "come and collect them from your storage."
                    : $"Storage holds {have} of {order.RemainingQuantity} matching items.");
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
