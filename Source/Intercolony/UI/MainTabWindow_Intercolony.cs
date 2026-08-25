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
    /// The market rows stay compact, but their actions are deliberate commitment boundaries:
    /// Accept creates a binding Sales Order and Counter opens the one bounded negotiation round.
    /// </summary>
    public partial class MainTabWindow_Intercolony : MainTabWindow
    {
        private Vector2 scrollPosition;

        private const float MarketMinRowHeight = 30f;
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

            /// <summary>Action column. Not sortable; holds Accept and Counter side by side.</summary>
            Accept = 8
        }

        /// <summary>
        /// Default sort is by soonest expiry: the listing's most time-critical information,
        /// and the one a player is most likely to act on.
        /// </summary>
        private Column sortColumn = Column.Expires;

        private bool sortDescending;

        // The selling tables and Labor's stacked views both need more room than 920x560 allowed.
        public override Vector2 RequestedTabSize => new Vector2(1040f, 620f);

        /// <summary>Which top-level view is showing (DESIGN.md §52, §53, §54).</summary>
        private enum Tab
        {
            /// <summary>
            /// §117's dashboard. First because by this point in the mod's life "how is the business
            /// doing" is the question a player opens the window to answer (§45).
            /// </summary>
            Business,

            Market,
            Orders,
            FindBuyer,

            // Procurement mirrors selling one for one, so the two directions of the same
            // business read the same way. Two of the four are placeholders on purpose: an
            // empty seat that says "not yet" is more honest than a missing one.
            SupplierMarket,
            FindSeller,
            PurchaseOrders,
            SupplyContracts,

            Labor,
            Contracts,
            Relations
        }

        /// <summary>
        /// Groups pages by the player's intent: selling keeps every outward-sales workflow
        /// together, while procurement stays separate so the direction of the money is legible.
        /// </summary>
        private enum TabGroup
        {
            Business,
            Selling,
            Procurement,
            Labor,
            Relations
        }

        private Tab tab = Tab.Business;
        private Tab sellingTab = Tab.Market;

        // Find seller remains the default procurement page so an existing colony opens on its
        // familiar RFQ workflow; the Supplier Market sits beside it in the same sub-tab row.
        private Tab procurementTab = Tab.FindSeller;

        // A page is latched off after a failure because DoWindowContents runs every frame; retrying
        // immediately would recreate both the exception and any half-finished GUI state forever.
        private readonly HashSet<Tab> failedPages = new HashSet<Tab>();
        private readonly HashSet<string> loggedPageFailures = new HashSet<string>();
        private int openPageScrollViews;
        private static bool debugThrowOnNextBusinessDraw;

        private Vector2 ordersScroll;

        /// <summary>Category filter (§53, §101). Null means "all categories".</summary>
        private IntercolonyProductCategory? categoryFilter;

        // --- Find Buyer (§12, §102) ---
        private Vector2 stockScroll;
        private Vector2 buyerScroll;
        private ThingDef selectedStockDef;
        private int selectedStockCount;
        private bool findBuyerAnimalMode;
        private List<AnimalStockGroup> animalStockCache;
        private AnimalStockGroup selectedAnimalStock;

        /// <summary>How much of the selected stock to offer. Drives saturation pricing (§13).</summary>
        private int sellQuantity;

        private List<BuyerOffer> findBuyerCache;

        /// <summary>
        /// Availability is UI freshness, not simulation state: real time keeps the scan rate
        /// stable at every game speed and still refreshes while paused.
        /// </summary>
        internal const float FindBuyerRefreshIntervalSeconds = 1.5f;

        private float stockCacheRefreshedAtRealtime;

        /// <summary>
        /// Colony stock is cached and rebuilt on entry, manually, or at a bounded real-time rate.
        ///
        /// <see cref="FindBuyerService.AvailableColonyStock"/> walks every Thing on the map.
        /// GUI code runs at least twice per frame (layout and repaint), so calling it
        /// unconditionally scanned a developed colony's entire thing list ~120 times a second
        /// and tanked the frame rate. Nothing here needs to be live to the tick.
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

        private enum QuoteColumn
        {
            Supplier = 0,
            Quantity = 1,
            UnitPrice = 2,
            Total = 3,
            LeadTime = 4,
            Fulfillment = 5,
            Distance = 6
        }

        private QuoteColumn quoteSortColumn = QuoteColumn.Quantity;
        private bool quoteSortDescending = true;

        private Vector2 supplierMarketScroll;
        private SupplierMarketColumn supplierMarketSortColumn = SupplierMarketColumn.TotalPayment;
        private bool supplierMarketSortDescending = true;
        private const float SupplierMarketRefreshIntervalSeconds = 0.5f;
        private List<SupplierMarketRow> supplierMarketRows;
        private List<float> supplierMarketRowHeights;
        private float supplierMarketRowHeightsWidth = -1f;
        private int supplierMarketRowsListingCount = -1;
        private float supplierMarketRowsBuiltAtRealtime;
        private SupplierMarketColumn supplierMarketRowsSortColumn;
        private bool supplierMarketRowsSortDescending;
        private bool supplierMarketRowsHaveSort;

        private PurchaseOrdersColumn purchaseOrdersSortColumn = PurchaseOrdersColumn.Timing;
        private bool purchaseOrdersSortDescending;

        private enum OrderColumn
        {
            Id = 0,
            Buyer = 1,
            Goods = 2,
            Quantity = 3,
            Value = 4,
            StatusEta = 5
        }

        private OrderColumn orderSortColumn = OrderColumn.StatusEta;
        private bool orderSortDescending;

        /// <summary>Minimum total value filter (§53 "minimum value").</summary>
        private int minValueFilter;

        public override void PreOpen()
        {
            base.PreOpen();

            // Reopening is an intentional retry boundary. A transient failure should not disable
            // its page for the rest of the play session.
            failedPages.Clear();
            loggedPageFailures.Clear();
            openPageScrollViews = 0;

            // Main-tab windows survive being closed. Eligibility can change while this one is
            // hidden as orders complete and reputation moves, so do not carry an old proposal
            // result into the next visit.
            contractProposalSettlementCache = null;
            expandedRelationSettlementId = NoExpandedRelation;
            ResetSupplierMarketCache();
        }

        public override void PostClose()
        {
            ResetSupplierMarketCache();
            base.PostClose();
        }

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

            DrawPageGuarded(body, state);
        }

        private void DrawPageGuarded(Rect inRect, IntercolonyWorldComponent state)
        {
            Tab drawingPage = tab;
            if (failedPages.Contains(drawingPage))
            {
                DrawPageFailure(inRect);
                return;
            }

            Exception drawException = null;
            Exception cleanupException = null;
            try
            {
                DrawPage(inRect, state);
            }
            catch (Exception ex)
            {
                drawException = ex;
                failedPages.Add(drawingPage);
            }
            finally
            {
                // A throw between BeginScrollView and EndScrollView otherwise poisons Verse's
                // mouse-position stack, and the altered text/colour state leaks into later windows.
                cleanupException = CloseOpenPageScrollViews();
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            if (drawException != null)
            {
                ReportPageFailure(drawingPage, drawException, "Could not draw page");
            }

            if (cleanupException != null)
            {
                failedPages.Add(drawingPage);
                ReportPageFailure(drawingPage, cleanupException,
                    "Could not restore the page's scroll view");
            }

            if (failedPages.Contains(drawingPage))
            {
                DrawPageFailure(inRect);
            }
        }

        private void DrawPage(Rect inRect, IntercolonyWorldComponent state)
        {

            if (tab == Tab.Business)
            {
                DrawBusiness(inRect, state);
                return;
            }

            if (tab == Tab.Orders)
            {
                DrawOrders(inRect, state);
                return;
            }

            if (tab == Tab.FindBuyer)
            {
                DrawFindBuyer(inRect, state);
                return;
            }

            if (tab == Tab.FindSeller)
            {
                DrawFindSeller(inRect, state);
                return;
            }

            if (tab == Tab.SupplierMarket)
            {
                DrawSupplierMarket(inRect, state);
                return;
            }

            if (tab == Tab.PurchaseOrders)
            {
                DrawProcurementOrders(inRect, state);
                return;
            }

            if (IsPlaceholderTab(tab))
            {
                DrawPlaceholderPage(inRect, tab, state);
                return;
            }

            if (tab == Tab.Contracts)
            {
                DrawContracts(inRect, state);
                return;
            }

            if (tab == Tab.Labor)
            {
                DrawLabor(inRect, state);
                return;
            }

            if (tab == Tab.Relations)
            {
                DrawRelations(inRect, state);
                return;
            }

            DrawMarket(inRect, state);
        }

        private void ReportPageFailure(Tab page, Exception exception, string context)
        {
            // Harmony returns the full enhanced stack only on its first formatting. Reusing that
            // text keeps the diagnostic that would otherwise be replaced by a duplicate ref.
            string exceptionText = exception.ToString();
            string key = page + "\n" + exception.GetType().FullName + "\n" + exceptionText;
            if (loggedPageFailures.Add(key))
            {
                IntercolonyLog.Error($"{context} '{page}': {exceptionText}");
            }
        }

        private static void DrawPageFailure(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.Label(inRect.ContractedBy(12f),
                "This page could not be drawn. Details are in the log.");
        }

        private void BeginPageScrollView(Rect outRect, ref Vector2 position, Rect viewRect)
        {
            Widgets.BeginScrollView(outRect, ref position, viewRect);
            openPageScrollViews++;
        }

        private void EndPageScrollView()
        {
            try
            {
                Widgets.EndScrollView();
            }
            finally
            {
                openPageScrollViews--;
            }
        }

        private Exception CloseOpenPageScrollViews()
        {
            Exception firstException = null;
            while (openPageScrollViews > 0)
            {
                try
                {
                    EndPageScrollView();
                }
                catch (Exception ex)
                {
                    if (firstException == null)
                    {
                        firstException = ex;
                    }
                }
            }

            return firstException;
        }

        internal static void ArmBusinessPageDrawFailureForDebug()
        {
            debugThrowOnNextBusinessDraw = true;
        }

        private static readonly TabGroup[] GroupOrder =
        {
            TabGroup.Business,
            TabGroup.Selling,
            TabGroup.Procurement,
            TabGroup.Labor,
            TabGroup.Relations
        };

        private static readonly Tab[] SellingTabOrder =
        {
            Tab.Market,
            Tab.FindBuyer,
            Tab.Orders,
            Tab.Contracts,
        };

        /// <summary>Deliberately the same shape and order as selling's.</summary>
        private static readonly Tab[] ProcurementTabOrder =
        {
            Tab.SupplierMarket,
            Tab.FindSeller,
            Tab.PurchaseOrders,
            Tab.SupplyContracts,
        };

        /// <summary>
        /// Pages that exist as a seat at the table and nothing more. They are drawn disabled
        /// with a tooltip saying so, because a tab that silently does nothing reads as broken.
        /// </summary>
        private static bool IsPlaceholderTab(Tab which)
        {
            return which == Tab.SupplyContracts;
        }

        /// <summary>Tab caption, including a count badge where one is useful.</summary>
        private static string TabLabel(Tab which, IntercolonyWorldComponent state)
        {
            switch (which)
            {
                case Tab.Business:
                    // No count badge. Every other tab counts things waiting on the player; this one
                    // is a report, and a number beside it would imply an inbox.
                    return "Business";
                case Tab.Orders:
                    int orders = state.OpenOrderCount;
                    return orders > 0 ? $"Orders ({orders})" : "Orders";
                case Tab.FindBuyer:
                    return "Find buyer";
                case Tab.Labor:
                    // An unpaid-wages badge on the tab itself, because §39's escalation is only
                    // playable if the player notices it without going looking.
                    if (PayrollService.TotalOwed(state) > 0)
                    {
                        return "Labor (!)";
                    }

                    // Applicants waiting are the other thing worth interrupting for: they have a
                    // patience and will go home unanswered, so a badge is the difference between a
                    // standing order working and quietly wasting itself (§35.2).
                    int waiting = state.WaitingApplicantCount;
                    if (waiting > 0)
                    {
                        return $"Labor ({waiting} applicant{(waiting == 1 ? "" : "s")})";
                    }

                    int employees = state.ActiveEmployeeCount;
                    return employees > 0 ? $"Labor ({employees})" : "Labor";
                case Tab.Contracts:
                    int contracts = state.ActiveContractCount;
                    return contracts > 0 ? $"Contracts ({contracts})" : "Contracts";
                case Tab.SupplierMarket:
                    return "Market";
                case Tab.FindSeller:
                    return "Find seller";
                case Tab.PurchaseOrders:
                    int purchases = OpenPurchaseCount(state);
                    return purchases > 0 ? $"Orders ({purchases})" : "Orders";
                case Tab.SupplyContracts:
                    return "Contracts";
                default:
                    return which.ToString();
            }
        }

        private static int OpenPurchaseCount(IntercolonyWorldComponent state)
        {
            int open = 0;
            List<PurchaseOrder> orders = state?.PurchaseOrders;
            for (int i = 0; orders != null && i < orders.Count; i++)
            {
                if (orders[i] != null && orders[i].IsOpen)
                {
                    open++;
                }
            }

            return open;
        }

        private static TabGroup GroupFor(Tab which)
        {
            switch (which)
            {
                case Tab.Market:
                case Tab.FindBuyer:
                case Tab.Orders:
                case Tab.Contracts:
                    return TabGroup.Selling;
                case Tab.SupplierMarket:
                case Tab.FindSeller:
                case Tab.PurchaseOrders:
                case Tab.SupplyContracts:
                    return TabGroup.Procurement;
                case Tab.Labor:
                    return TabGroup.Labor;
                case Tab.Relations:
                    return TabGroup.Relations;
                default:
                    return TabGroup.Business;
            }
        }

        private static string GroupLabel(TabGroup which, IntercolonyWorldComponent state)
        {
            switch (which)
            {
                case TabGroup.Selling:
                    // Orders and contracts are the only selling children that report badges.
                    int selling = state.OpenOrderCount + state.ActiveContractCount;
                    return selling > 0 ? $"Selling ({selling})" : "Selling";
                case TabGroup.Procurement:
                    // Mirrors selling's badge: open requests plus purchases already placed.
                    int procurement = state.OpenRequestCount + OpenPurchaseCount(state);
                    return procurement > 0 ? $"Procurement ({procurement})" : "Procurement";
                case TabGroup.Labor:
                    return TabLabel(Tab.Labor, state);
                case TabGroup.Relations:
                    return TabLabel(Tab.Relations, state);
                default:
                    return TabLabel(Tab.Business, state);
            }
        }

        private float DrawTabSelector(Rect inRect, IntercolonyWorldComponent state)
        {
            const float ButtonHeight = 30f;
            const float Gap = 6f;
            const float RowSpacing = 8f;

            Text.Font = GameFont.Small;
            string[] labels = new string[GroupOrder.Length];
            for (int i = 0; i < GroupOrder.Length; i++)
            {
                labels[i] = GroupLabel(GroupOrder[i], state);
            }

            float[] widths = MeasureTabWidths(inRect.width, labels, Gap);
            TabGroup selectedGroup = GroupFor(tab);
            float x = inRect.x;
            for (int i = 0; i < GroupOrder.Length; i++)
            {
                TabGroup which = GroupOrder[i];
                Rect rect = new Rect(x, 0f, widths[i], ButtonHeight);
                if (Widgets.ButtonText(rect, labels[i], drawBackground: selectedGroup != which))
                {
                    SelectGroup(which, state);
                }

                x += widths[i] + Gap;
            }

            float consumedHeight = ButtonHeight + RowSpacing;
            if (GroupFor(tab) == TabGroup.Selling)
            {
                DrawSubTabs(
                    new Rect(inRect.x, consumedHeight, inRect.width, ButtonHeight),
                    SellingTabOrder, state);
                consumedHeight += ButtonHeight + RowSpacing;
            }
            else if (GroupFor(tab) == TabGroup.Procurement)
            {
                DrawSubTabs(
                    new Rect(inRect.x, consumedHeight, inRect.width, ButtonHeight),
                    ProcurementTabOrder, state);
                consumedHeight += ButtonHeight + RowSpacing;
            }

            return consumedHeight;
        }

        private void DrawSubTabs(Rect rect, Tab[] order, IntercolonyWorldComponent state)
        {
            const float Gap = 4f;

            string[] labels = new string[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                labels[i] = TabLabel(order[i], state);
            }

            float[] widths = MeasureTabWidths(rect.width, labels, Gap);
            float x = rect.x;
            for (int i = 0; i < order.Length; i++)
            {
                Tab which = order[i];
                Rect buttonRect = new Rect(x, rect.y, widths[i], rect.height);
                bool placeholder = IsPlaceholderTab(which);

                if (Widgets.ButtonText(
                        buttonRect, labels[i], drawBackground: tab != which,
                        active: !placeholder) && !placeholder)
                {
                    SelectTab(which, state);
                }

                if (placeholder && ShouldBuildTooltip(buttonRect))
                {
                    TooltipHandler.TipRegion(buttonRect, "Under development.");
                }

                x += widths[i] + Gap;
            }

            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
        }

        private static float[] MeasureTabWidths(float rowWidth, string[] labels, float gap)
        {
            const float HorizontalPadding = 24f;

            float usableWidth = Mathf.Max(0f, rowWidth - gap * (labels.Length - 1));
            float totalWidth = 0f;
            float[] widths = new float[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                widths[i] = Text.CalcSize(labels[i]).x + HorizontalPadding;
                totalWidth += widths[i];
            }

            if (totalWidth > usableWidth && totalWidth > 0f)
            {
                float scale = usableWidth / totalWidth;
                for (int i = 0; i < widths.Length; i++)
                {
                    widths[i] *= scale;
                }
            }

            return widths;
        }

        private void SelectGroup(TabGroup which, IntercolonyWorldComponent state)
        {
            switch (which)
            {
                case TabGroup.Selling:
                    SelectTab(sellingTab, state);
                    break;
                case TabGroup.Procurement:
                    SelectTab(procurementTab, state);
                    break;
                case TabGroup.Labor:
                    SelectTab(Tab.Labor, state);
                    break;
                case TabGroup.Relations:
                    SelectTab(Tab.Relations, state);
                    break;
                default:
                    SelectTab(Tab.Business, state);
                    break;
            }
        }

        private void SelectTab(Tab which, IntercolonyWorldComponent state)
        {
            if (tab != which)
            {
                ResetSupplierMarketCache();
            }

            tab = which;

            if (GroupFor(which) == TabGroup.Selling)
            {
                sellingTab = which;
            }

            if (GroupFor(which) == TabGroup.Procurement && !IsPlaceholderTab(which))
            {
                procurementTab = which;
            }

            if (which == Tab.FindBuyer)
            {
                // Re-scan once on entry so the list is current without being live.
                stockCache = null;
                animalStockCache = null;
                findBuyerCache = null;
            }
            else if (which == Tab.Contracts)
            {
                contractProposalSettlementCache = null;
            }
            else if (which == Tab.Labor)
            {
                // Cheap: the pool is cached per market refresh and only built when stale.
                LaborCandidateService.Refresh(state);
            }
        }

        internal void ShowOrdersTab()
        {
            SelectTab(Tab.Orders, IntercolonyWorldComponent.Current);
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
                string emptyMessage = totalAvailable == 0
                        ? "No settlement is currently asking for anything.\n" +
                          "Demand is refreshed periodically as the world turns."
                        : $"All {totalAvailable} current offers are beyond your distance limit.\n" +
                          "Raise the limit above to see them.";
                Widgets.Label(new Rect(0f, y, inRect.width, Text.CalcHeight(emptyMessage, inRect.width)), emptyMessage);
                GUI.color = Color.white;
                return;
            }

            Sort(live);

            DrawHeaderRow(new Rect(0f, y, inRect.width - 16f, HeaderHeight));
            y += HeaderHeight;
            Widgets.DrawLineHorizontal(0f, y, inRect.width);
            y += 2f;

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            float viewWidth = inRect.width - 16f;
            float contentHeight = 0f;
            foreach (MarketOpportunity opportunity in live)
            {
                contentHeight += MarketRowHeight(opportunity, viewWidth);
            }

            Rect viewRect = new Rect(0f, 0f, viewWidth, contentHeight);

            BeginPageScrollView(outRect, ref scrollPosition, viewRect);
            float rowY = 0f;
            for (int i = 0; i < live.Count; i++)
            {
                float rowHeight = MarketRowHeight(live[i], viewRect.width);
                DrawRow(new Rect(0f, rowY, viewRect.width, rowHeight), live[i], i);
                rowY += rowHeight;
            }

            EndPageScrollView();
        }

        /// <summary>
        /// Column layout, as fractions of the available width. Kept in one place so the header
        /// and the rows cannot drift apart.
        /// </summary>
        private static readonly float[] ColumnWidths =
            { 0.15f, 0.21f, 0.06f, 0.08f, 0.09f, 0.08f, 0.08f, 0.09f, 0.16f };

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
            MarketTableSortUtility.Sort(
                list, comparison, sortDescending, (a, b) => a.id.CompareTo(b.id));
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
            if (ShouldBuildTooltip(rect))
            {
                TooltipHandler.TipRegion(rect, BuildListingTooltip(opportunity));
            }

            Rect Cell(int i)
            {
                float cellX = 0f;
                for (int k = 0; k < i; k++)
                {
                    cellX += rect.width * ColumnWidths[k];
                }

                // Leave a gutter so a long value abuts the next column instead of running
                // underneath it. RimWorld clips labels to their rect, so this is what turns
                // an overlapping mess into honest truncation.
                return new Rect(cellX, rect.y + 4f, rect.width * ColumnWidths[i] - 4f,
                    rect.height - 8f);
            }

            Widgets.Label(Cell(0), MarketCellLabel(opportunity, 0));
            Widgets.Label(Cell(1), MarketCellLabel(opportunity, 1));
            Widgets.Label(Cell(2), MarketCellLabel(opportunity, 2));
            Widgets.Label(Cell(3), MarketCellLabel(opportunity, 3));
            Widgets.Label(Cell(4), MarketCellLabel(opportunity, 4));
            Widgets.Label(Cell(5), MarketCellLabel(opportunity, 5));

            float days = opportunity.DaysRemaining;
            GUI.color = days < 1.5f ? Color.yellow : Color.white;
            Widgets.Label(Cell(6), MarketCellLabel(opportunity, 6));
            GUI.color = Color.white;

            GUI.color = opportunity.fulfillment == FulfillmentMode.BuyerPickup
                ? new Color(0.6f, 0.85f, 1f)
                : Color.white;
            Widgets.Label(Cell(7), MarketCellLabel(opportunity, 7));
            GUI.color = Color.white;

            // Keep both commitment choices in the action column. A counter button must disappear
            // after the service records its response, or a stale row would invite a second round.
            Rect actionCell = Cell((int)Column.Accept);
            const float ActionHeight = 23f;
            const float ActionGap = 4f;
            float actionWidth = (actionCell.width - ActionGap) / 2f;
            Rect firstActionRect = new Rect(
                actionCell.x,
                rect.y + (rect.height - ActionHeight) / 2f,
                actionWidth,
                ActionHeight);
            Rect secondActionRect = new Rect(
                actionCell.x + actionWidth + ActionGap,
                firstActionRect.y,
                actionWidth,
                ActionHeight);

            if (opportunity.HasPendingCounterpartyCounter)
            {
                if (Widgets.ButtonText(actionCell, "Answer"))
                {
                    OpenCounterofferAnswer(opportunity);
                }
                return;
            }

            if (opportunity.CanAcceptOriginalTerms && Widgets.ButtonText(firstActionRect, "Accept"))
            {
                AcceptOpportunity(opportunity);
            }

            if (CounterofferUiService.CounterActionAvailable(opportunity) &&
                Widgets.ButtonText(secondActionRect, "Counter"))
            {
                OpenCounterofferDialog(opportunity);
            }
        }

        private void OpenCounterofferDialog(MarketOpportunity opportunity)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_Counteroffer(state, opportunity));
        }

        private void OpenCounterofferAnswer(MarketOpportunity opportunity)
        {
            // A pending final counter is persisted, but its ephemeral evaluator result is not an
            // entity. The read-only answer uses the persisted terms; acceptance still uses the
            // service's exact final-counter boundary rather than reconstructing a proposal.
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null || !opportunity.TryGetFinalCounterTerms(
                    out IntercolonyNegotiationTerms finalTerms))
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_Counteroffer(
                state,
                opportunity,
                CounterofferUiService.BuildPersistedFinalCounterView(opportunity, finalTerms),
                acceptedOrder: null));
        }

        private static float MarketRowHeight(MarketOpportunity opportunity, float tableWidth)
        {
            float height = MarketMinRowHeight;
            for (int i = 0; i < (int)Column.Accept; i++)
            {
                float cellWidth = tableWidth * ColumnWidths[i] - 4f;
                height = Mathf.Max(height,
                    Text.CalcHeight(MarketCellLabel(opportunity, i), cellWidth) + 8f);
            }

            return height;
        }

        private static string MarketCellLabel(MarketOpportunity opportunity, int column)
        {
            switch ((Column)column)
            {
                case Column.Buyer: return opportunity.settlementName;
                case Column.Item: return opportunity.ItemLabel();
                case Column.Quantity: return opportunity.quantity.ToString();
                case Column.UnitPrice: return opportunity.unitPrice.ToString("F2");
                case Column.TotalPrice: return opportunity.TotalPrice.ToString();
                case Column.Distance:
                    return opportunity.distanceTiles < 0f ? "?" : $"{opportunity.distanceTiles:F0} t";
                case Column.Expires: return $"{opportunity.DaysRemaining:F1}d";
                case Column.Deadline:
                    return opportunity.fulfillment == FulfillmentMode.BuyerPickup
                        ? BuyerPickupTimingLabel(opportunity.distanceTiles)
                        : $"{opportunity.deadlineDays}d haul";
                default: return "";
            }
        }

        internal static string BuyerPickupTimingLabel(float distanceTiles)
        {
            int days = SalesOrderService.EstimateBuyerPickupTravelDays(distanceTiles);
            return $"~{days}d pickup";
        }

        internal static string BuyerPickupTimingExplanation(
            string settlementName, int deadlineDays, float distanceTiles)
        {
            int pickupDays = SalesOrderService.EstimateBuyerPickupTravelDays(distanceTiles);
            string buyer = settlementName.NullOrEmpty() ? "The buyer" : settlementName;
            return $"Mark the goods ready within {deadlineDays} days of accepting. " +
                   $"Once marked ready, {buyer} is expected to take approximately " +
                   $"{pickupDays} days to arrive and collect them.";
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
            sb.AppendLine(opportunity.fulfillment == FulfillmentMode.BuyerPickup
                ? BuyerPickupTimingExplanation(
                    opportunity.settlementName, opportunity.deadlineDays, opportunity.distanceTiles)
                : $"Deliver within {opportunity.deadlineDays} days of accepting.");

            if (opportunity.minQuality.HasValue)
            {
                sb.AppendLine($"Only items of {opportunity.minQuality.Value.GetLabel()} quality " +
                              "or better will be accepted.");
            }

            if (opportunity.stuffDef != null)
            {
                sb.AppendLine($"Must be made of {opportunity.stuffDef.label}.");
            }

            if (opportunity.HasConditionConstraint)
            {
                sb.AppendLine($"Items below " +
                              $"{Mathf.RoundToInt(opportunity.minHitPointsPercent * 100f)}% condition " +
                              "will be refused at delivery.");
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

            string economy = SettlementEconomyDisplay.SettlementEconomicSummary(
                opportunity.settlementId);
            if (!economy.NullOrEmpty())
            {
                sb.AppendLine();
                sb.Append(economy);
            }

            sb.AppendLine();
            sb.Append(opportunity.priceExplanation);
            return sb.ToString();
        }

        /// <summary>
        /// Dynamic tooltip text can be substantial. TooltipHandler applies the same gate after it
        /// receives the text, which is too late to avoid building every row's string.
        /// </summary>
        private static bool ShouldBuildTooltip(Rect rect)
        {
            return Event.current.type == EventType.Repaint &&
                   (Mouse.IsOver(rect) || DebugViewSettings.drawTooltipEdges);
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
                    SalesOrder preview = BuildOpportunityPaymentPreview(state, opportunity, qty);
                    string partialTip = qty < opportunity.quantity
                        ? $"The buyer asked for {opportunity.quantity}. Committing to {qty} is a " +
                          "smaller deal, not a partial one — you owe exactly what you accept."
                        : null;
                    string paymentTip = $"{preview.unitPrice:F2} each" +
                        (qty < opportunity.quantity
                            ? ". A smaller lot earns a better rate per unit."
                            : "");
                    string tiles = opportunity.distanceTiles < 0f
                        ? "unknown"
                        : $"{opportunity.distanceTiles:F0}";
                    List<TermRow> rows = new List<TermRow>
                    {
                        new TermRow(null,
                            $"Accept {qty}x {opportunity.ItemLabel()} for " +
                            $"{opportunity.settlementName}", partialTip),
                        new TermRow("Payment", $"{preview.TotalPayment} silver", paymentTip),
                        new TermRow("Distance",
                            opportunity.distanceTiles < 0f ? "unknown" : $"{tiles} tiles")
                    };

                    if (opportunity.minQuality.HasValue)
                    {
                        string quality = opportunity.minQuality.Value.GetLabel();
                        rows.Add(new TermRow("Quality", $"{quality} or better",
                            $"Only items of {quality} quality or better will be accepted."));
                    }

                    if (opportunity.stuffDef != null)
                    {
                        rows.Add(new TermRow("Material", opportunity.stuffDef.LabelCap,
                            $"The goods must be made of {opportunity.stuffDef.label}."));
                    }

                    if (opportunity.HasConditionConstraint)
                    {
                        int condition = Mathf.RoundToInt(opportunity.minHitPointsPercent * 100f);
                        rows.Add(new TermRow("Condition", $"{condition}% or better",
                            $"Items below {condition}% condition will be refused at delivery."));
                    }

                    if (opportunity.fulfillment == FulfillmentMode.BuyerPickup)
                    {
                        int pickupDays = SalesOrderService.EstimateBuyerPickupTravelDays(
                            opportunity.distanceTiles);
                        string distanceBasis = opportunity.distanceTiles < 0f
                            ? "an unknown distance"
                            : $"{tiles} tiles";
                        rows.Add(new TermRow("Fulfilment", "Buyer collects",
                            "No caravan is needed; the buyer handles collection and pays less for it."));
                        rows.Add(new TermRow("Mark ready by",
                            $"{opportunity.deadlineDays} days from now",
                            "A fixed deadline to declare the goods ready. It does not depend on distance."));
                        rows.Add(new TermRow("Buyer arrives",
                            $"about {pickupDays} days after you mark ready",
                            $"Travel time from {opportunity.settlementName}, estimated from " +
                            $"{distanceBasis}."));
                    }
                    else
                    {
                        rows.Add(new TermRow("Fulfilment", "You deliver",
                            "You deliver by caravan. Missing the deadline fails the order."));
                        rows.Add(new TermRow("Deliver within",
                            $"{opportunity.deadlineDays} days"));
                    }

                    return rows;
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

        private static SalesOrder BuildOpportunityPaymentPreview(
            IntercolonyWorldComponent state, MarketOpportunity opportunity, int quantity)
        {
            return new SalesOrder
            {
                line = new OrderLine(opportunity.thingDef, quantity),
                unitPrice = IntercolonyPricing.RepriceForQuantity(
                    state, opportunity, ProfileFor(state, opportunity.settlementId), quantity, out _)
            };
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
                string emptyMessage = "No orders yet. Accept an offer in the Market tab to commit to one.";
                Widgets.Label(new Rect(0f, y, inRect.width, Text.CalcHeight(emptyMessage, inRect.width)), emptyMessage);
                GUI.color = Color.white;
                return;
            }

            SortOrders(orders);

            int closedCount = orders.Count(order => !order.IsOpen);
            int clearableCount = closedCount > 0
                ? OrderHistoryService.CountClearableSalesOrderHistory(state)
                : 0;

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            float contentHeight = OrderHeaderHeight + orders.Count * OrderRowHeight +
                                  (closedCount > 0 ? ClosedOrderSectionHeaderHeight : 0f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);

            BeginPageScrollView(outRect, ref ordersScroll, viewRect);
            float rowY = 0f;
            if (closedCount > 0)
            {
                DrawClosedSalesOrderHeader(
                    viewRect.width, rowY, closedCount, clearableCount, state);
                rowY += ClosedOrderSectionHeaderHeight;
            }

            DrawOrderHeader(new Rect(0f, rowY, viewRect.width, OrderHeaderHeight));
            rowY += OrderHeaderHeight;
            for (int i = 0; i < orders.Count; i++)
            {
                DrawOrderRow(new Rect(0f, rowY, viewRect.width, OrderRowHeight), orders[i], i);
                rowY += OrderRowHeight;
            }

            EndPageScrollView();
        }

        private static void DrawClosedSalesOrderHeader(
            float width, float y, int closedCount, int clearableCount,
            IntercolonyWorldComponent state)
        {
            const float buttonWidth = 190f;
            Widgets.Label(new Rect(0f, y + 3f, width - buttonWidth - 8f, 24f),
                $"Closed orders ({closedCount})");

            if (clearableCount <= 0)
            {
                return;
            }

            Rect clearRect = new Rect(width - buttonWidth, y, buttonWidth, 28f);
            if (Widgets.ButtonText(clearRect, "Clear completed history"))
            {
                string orderWord = clearableCount == 1 ? "order" : "orders";
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Remove {clearableCount} closed sales {orderWord} from this list?\n\n" +
                    "Active orders and orders still tied to an agreement or completed during " +
                    "the current market refresh will be kept, along with your trading record.",
                    () => OrderHistoryService.ClearSalesOrderHistory(state),
                    destructive: true));
            }
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

            Rect goodsModeRect = new Rect(0f, y, 110f, 28f);
            Rect animalsModeRect = new Rect(116f, y, 110f, 28f);
            if (Widgets.ButtonText(goodsModeRect, "Goods") && findBuyerAnimalMode)
            {
                findBuyerAnimalMode = false;
                findBuyerCache = null;
            }
            if (Widgets.ButtonText(animalsModeRect, "Animals") && !findBuyerAnimalMode)
            {
                findBuyerAnimalMode = true;
                findBuyerCache = null;
            }
            Widgets.DrawHighlightSelected(findBuyerAnimalMode ? animalsModeRect : goodsModeRect);
            y += 34f;

            Map map = Find.CurrentMap;
            if (map == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, inRect.width, 40f),
                    "Open a colony map to search your stock.");
                GUI.color = Color.white;
                return;
            }

            // The null check gives entry/manual invalidation an instant rebuild. The real-time
            // gate bounds ongoing scans even though layout and repaint both execute this method.
            bool missingVisibleCache = findBuyerAnimalMode
                ? animalStockCache == null
                : stockCache == null;
            if (missingVisibleCache || FindBuyerRefreshDue(
                    stockCacheRefreshedAtRealtime, Time.realtimeSinceStartup))
            {
                RefreshFindBuyerStock(state, map);
            }

            // Sits to the right of the heading, not under it.
            Rect refreshRect = new Rect(inRect.width - 124f, y - 32f, 110f, 26f);
            if (Widgets.ButtonText(refreshRect, "Refresh"))
            {
                RefreshFindBuyerStock(state, map);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            TooltipHandler.TipRegion(refreshRect,
                "Refresh storage and existing commitments now. Availability also updates " +
                "automatically every 1.5 seconds while this page is visible.");

            bool empty = findBuyerAnimalMode
                ? animalStockCache.Count == 0
                : stockCache.Count == 0;
            if (empty)
            {
                GUI.color = Color.gray;
                string emptyMessage = findBuyerAnimalMode
                        ? "No eligible colony animals are currently available to sell.\n" +
                          "Humanlikes, hosted pawns and animals already committed are excluded."
                        : "Nothing tradeable is currently available to sell.\n" +
                          "Counts include only stockpiled goods that are not already committed.";
                Widgets.Label(new Rect(0f, y, inRect.width, Text.CalcHeight(emptyMessage, inRect.width)), emptyMessage);
                GUI.color = Color.white;
                return;
            }

            float listWidth = Mathf.Min(300f, inRect.width * 0.34f);
            Rect stockRect = new Rect(0f, y, listWidth, inRect.yMax - y);
            Rect offersRect = new Rect(listWidth + 12f, y, inRect.width - listWidth - 12f, inRect.yMax - y);

            if (findBuyerAnimalMode)
            {
                DrawAnimalStockList(stockRect, animalStockCache);
            }
            else
            {
                DrawStockList(stockRect, stockCache);
            }
            DrawBuyerOffers(offersRect, state);
        }

        private void RefreshFindBuyerStock(IntercolonyWorldComponent state, Map map)
        {
            if (findBuyerAnimalMode)
            {
                animalStockCache = FindBuyerService.AvailableColonyAnimals(state, map);
                ReconcileAnimalSelection();
            }
            else
            {
                stockCache = FindBuyerService.AvailableColonyStock(state, map);
                ReconcileFindBuyerSelection(stockCache, ref selectedStockDef,
                    ref selectedStockCount, ref sellQuantity, ref findBuyerCache);
            }

            // Record completion rather than start time so even an unusually slow scan cannot
            // make layout/repaint immediately run a second one.
            stockCacheRefreshedAtRealtime = Time.realtimeSinceStartup;
        }

        private void ReconcileAnimalSelection()
        {
            findBuyerCache = null;
            if (selectedAnimalStock == null)
            {
                return;
            }

            foreach (AnimalStockGroup group in animalStockCache)
            {
                if (SameAnimalGroup(group, selectedAnimalStock))
                {
                    selectedAnimalStock = group;
                    selectedStockCount = group.quantity;
                    sellQuantity = Mathf.Clamp(sellQuantity, 1, group.quantity);
                    return;
                }
            }

            selectedAnimalStock = null;
            selectedStockCount = 0;
            sellQuantity = 0;
        }

        private static bool SameAnimalGroup(AnimalStockGroup a, AnimalStockGroup b)
        {
            return a?.race == b?.race && a?.spec != null && b?.spec != null &&
                   a.spec.gender == b.spec.gender &&
                   a.spec.lifeStage == b.spec.lifeStage &&
                   a.spec.pregnant == b.spec.pregnant;
        }

        internal static bool FindBuyerRefreshDue(float lastRefreshTime, float now)
        {
            return now - lastRefreshTime >= FindBuyerRefreshIntervalSeconds;
        }

        internal static void ReconcileFindBuyerSelection(
            List<KeyValuePair<ThingDef, int>> stock,
            ref ThingDef selectedDef,
            ref int selectedCount,
            ref int quantity,
            ref List<BuyerOffer> offers)
        {
            // Prices depend on availability, so every stock refresh invalidates the matching
            // offer snapshot as one operation with the selection reconciliation.
            offers = null;

            if (selectedDef == null)
            {
                return;
            }

            foreach (KeyValuePair<ThingDef, int> entry in stock)
            {
                if (entry.Key == selectedDef)
                {
                    selectedCount = entry.Value;
                    quantity = Mathf.Clamp(quantity, 1, entry.Value);
                    return;
                }
            }

            selectedDef = null;
            selectedCount = 0;
            quantity = 0;
        }

        private void DrawStockList(Rect rect, List<KeyValuePair<ThingDef, int>> stock)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "Available to sell");
            Rect outRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.height - 26f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, stock.Count * 28f);

            BeginPageScrollView(outRect, ref stockScroll, viewRect);
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

            EndPageScrollView();
        }

        private void DrawAnimalStockList(Rect rect, List<AnimalStockGroup> stock)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "Eligible animals");
            Rect outRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.height - 26f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, stock.Count * 42f);

            BeginPageScrollView(outRect, ref stockScroll, viewRect);
            float rowY = 0f;
            foreach (AnimalStockGroup group in stock)
            {
                Rect row = new Rect(0f, rowY, viewRect.width, 42f);
                if (SameAnimalGroup(selectedAnimalStock, group))
                {
                    Widgets.DrawHighlightSelected(row);
                }

                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(row.x + 4f, row.y + 2f, row.width - 70f, 22f),
                    group.race.LabelCap);
                GUI.color = Color.gray;
                Widgets.Label(new Rect(row.x + 4f, row.y + 20f, row.width - 70f, 20f),
                    group.spec.ShortLabel(group.race));
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.xMax - 64f, row.y + 9f, 60f, 24f),
                    group.quantity.ToString());

                if (Widgets.ButtonInvisible(row))
                {
                    selectedAnimalStock = group;
                    selectedStockCount = group.quantity;
                    sellQuantity = group.quantity;
                    findBuyerCache = null;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }

                rowY += 42f;
            }

            EndPageScrollView();
        }

        private void DrawBuyerOffers(Rect rect, IntercolonyWorldComponent state)
        {
            bool noSelection = findBuyerAnimalMode
                ? selectedAnimalStock == null
                : selectedStockDef == null;
            if (noSelection)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rect.x, rect.y + 4f, rect.width, 40f),
                    "Select something from your storage to see who wants it.");
                GUI.color = Color.white;
                return;
            }

            float y = rect.y;

            Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
                findBuyerAnimalMode
                    ? $"Buyers for {selectedStockCount}x " +
                      selectedAnimalStock.spec.ShortLabel(selectedAnimalStock.race)
                    : $"Buyers for {selectedStockCount}x {selectedStockDef.LabelCap}");
            y += 28f;

            if (!findBuyerAnimalMode && selectedStockDef != null)
            {
                ProductBrandUiService.SpecificGoodDetails brand =
                    ProductBrandUiService.BuildSpecificGoodDetails(state, selectedStockDef);
                y = DrawSpecificGoodBrandDetails(rect, y, brand);
            }

            // Searching walks every accessible settlement and prices each one. Cached, and
            // invalidated when the selection, quantity, or availability snapshot changes (§84).
            if (findBuyerCache == null)
            {
                findBuyerCache = findBuyerAnimalMode
                    ? FindBuyerService.FindAnimalBuyers(
                        state, selectedAnimalStock.race, selectedAnimalStock.spec, sellQuantity)
                    : FindBuyerService.FindBuyers(
                        state, Find.CurrentMap ?? Find.AnyPlayerHomeMap,
                        selectedStockDef, null, sellQuantity);
                SortBuyers(findBuyerCache);
            }

            DrawBuyerHeader(new Rect(rect.x, y, rect.width - 16f, 24f));
            y += 24f;
            Widgets.DrawLineHorizontal(rect.x, y, rect.width);
            y += 2f;

            Rect outRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, findBuyerCache.Count * 34f);

            BeginPageScrollView(outRect, ref buyerScroll, viewRect);
            float rowY = 0f;
            for (int i = 0; i < findBuyerCache.Count; i++)
            {
                DrawBuyerRow(new Rect(0f, rowY, viewRect.width, 34f), findBuyerCache[i], i, state);
                rowY += 34f;
            }

            EndPageScrollView();
        }

        private static float DrawSpecificGoodBrandDetails(
            Rect inRect, float y, ProductBrandUiService.SpecificGoodDetails details)
        {
            float contentWidth = Mathf.Max(1f, inRect.width - 12f);
            float labelWidth = Mathf.Min(190f, contentWidth * 0.42f);
            float valueWidth = Mathf.Max(1f, contentWidth - labelWidth - 12f);
            float valueX = inRect.x + labelWidth + 12f;

            string strengthLabel = "Relevant brand strength";
            float strengthLabelHeight = Text.CalcHeight(strengthLabel, labelWidth);
            float strengthValueHeight = Text.CalcHeight(details.strengthLabel, valueWidth);
            float strengthHeight = Mathf.Max(strengthLabelHeight, strengthValueHeight);
            Widgets.Label(new Rect(inRect.x, y, labelWidth, strengthLabelHeight), strengthLabel);
            Widgets.Label(new Rect(valueX, y, valueWidth, strengthValueHeight), details.strengthLabel);
            y += strengthHeight + 3f;

            string basisLabel = "Brand basis";
            float basisLabelHeight = Text.CalcHeight(basisLabel, labelWidth);
            float basisValueHeight = Text.CalcHeight(details.attribution, valueWidth);
            float basisHeight = Mathf.Max(basisLabelHeight, basisValueHeight);
            Rect basisRect = new Rect(inRect.x, y, contentWidth, basisHeight);
            Widgets.Label(new Rect(inRect.x, y, labelWidth, basisLabelHeight), basisLabel);
            Widgets.Label(new Rect(valueX, y, valueWidth, basisValueHeight), details.attribution);
            TooltipHandler.TipRegion(basisRect, details.tooltip);
            Widgets.DrawHighlightIfMouseover(basisRect);

            return y + basisHeight + 7f;
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

            if (ShouldBuildTooltip(rect))
            {
                TooltipHandler.TipRegion(rect,
                    $"{offer.settlement?.Label} would take up to {offer.maxQuantity} " +
                    $"{(offer.IsAnimalOffer ? offer.animalSpec.ShortLabel(offer.def) : selectedStockDef?.label)}.\n" +
                    $"Selling {offer.quantity} at {offer.unitPrice:F2} each = {offer.TotalPrice} silver.\n\n" +
                    (offer.IsAnimalOffer
                        ? IntercolonyPricing.Explain(
                            offer.def, null, offer.animalSpec, offer.quantity,
                            offer.unitPrice, offer.factors)
                        : IntercolonyPricing.Explain(
                            offer.def, offer.stuff, offer.knownInventory, offer.quantity,
                            offer.unitPrice, offer.factors)));
            }

            Rect sellRect = new Rect(rect.xMax - 84f, rect.y + 4f, 78f, 26f);
            if (Widgets.ButtonText(sellRect, "Sell"))
            {
                ConfirmSell(state, offer);
            }
        }

        private void ConfirmSell(IntercolonyWorldComponent state, BuyerOffer offer)
        {
            const int DeadlineDays = 12;

            if (offer.IsAnimalOffer)
            {
                ConfirmAnimalSell(state, offer, DeadlineDays);
                return;
            }

            Find.WindowStack.Add(new Dialog_ConfirmQuantity(
                "Sell to this buyer?",
                "Create order",
                offer.quantity,
                (qty, fulfillment, discountFraction) =>
                {
                    SalesOrder preview = BuildSalePaymentPreview(
                        state, offer, qty, fulfillment, discountFraction);
                    int waived = preview.TotalPayment - preview.DiscountedTotalPayment;
                    string tiles = offer.distanceTiles < 0f
                        ? "unknown"
                        : $"{offer.distanceTiles:F0}";
                    string paymentTip = $"{preview.unitPrice:F2} each before discount. Waived: " +
                        $"{waived} silver. The waived value improves your standing with the " +
                        "buyer's faction." +
                        (qty < offer.quantity
                            ? " A smaller lot earns a better rate per unit."
                            : "");
                    ProductBrandUiService.SpecificGoodDetails brand =
                        ProductBrandUiService.BuildSpecificGoodDetails(state, offer.def);
                    List<TermRow> rows = new List<TermRow>
                    {
                        new TermRow(null,
                            $"Sell {qty}x {offer.def.LabelCap} to {offer.settlement?.Label}"),
                        new TermRow("Relevant brand strength", brand.strengthLabel),
                        new TermRow("Brand basis", brand.attribution, brand.tooltip),
                        new TermRow("Payment", $"{preview.DiscountedTotalPayment} silver", paymentTip),
                        new TermRow("Distance",
                            offer.distanceTiles < 0f ? "unknown" : $"{tiles} tiles")
                    };

                    if (fulfillment == FulfillmentMode.BuyerPickup)
                    {
                        int pickupDays = SalesOrderService.EstimateBuyerPickupTravelDays(
                            offer.distanceTiles);
                        string distanceBasis = offer.distanceTiles < 0f
                            ? "an unknown distance"
                            : $"{tiles} tiles";
                        rows.Add(new TermRow("Fulfilment", "Buyer collects",
                            "No caravan is needed; the buyer handles collection and pays less for it."));
                        rows.Add(new TermRow("Mark ready by", $"{DeadlineDays} days from now",
                            "A fixed deadline to declare the goods ready. It does not depend on distance."));
                        rows.Add(new TermRow("Buyer arrives",
                            $"about {pickupDays} days after you mark ready",
                            $"Travel time from {offer.settlement?.Label}, estimated from {distanceBasis}."));
                    }
                    else
                    {
                        rows.Add(new TermRow("Fulfilment", "You deliver",
                            "A caravan trip, paid at a premium for taking it on."));
                        rows.Add(new TermRow("Deliver within", $"{DeadlineDays} days"));
                    }

                    rows.Add(new TermRow("Commitment", "Binding",
                        "The quantity counts against what Find Buyer considers available, so it " +
                        "will not be offered to another buyer. The goods are not physically locked: " +
                        "your colony can still consume or move them, and you are responsible for " +
                        "having them ready."));
                    return rows;
                },
                (qty, fulfillment, discountFraction, markReadyNow) =>
                {
                    BuyerOffer priced = offer;
                    Map fulfillmentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                    priced.unitPrice = FindBuyerService.SellRateFor(
                        state, offer, qty, fulfillment, fulfillmentMap);
                    if (fulfillment == FulfillmentMode.BuyerPickup && markReadyNow)
                    {
                        SalesOrder pending = SalesOrderService.BuildOrderFromOffer(
                            priced, qty, DeadlineDays, fulfillment, fulfillmentMap);
                        if (!SalesOrderService.CanMarkReadyNow(
                                pending, fulfillmentMap, out string reason))
                        {
                            Messages.Message(
                                $"Order not created: {reason}\n" +
                                "Untick \"Mark ready now\" to create the order and ready it later.",
                                MessageTypeDefOf.RejectInput, historical: false);
                            return false;
                        }
                    }

                    SalesOrder order = SalesOrderService.CreateFromOffer(
                        state, fulfillmentMap, priced, qty,
                        DeadlineDays, fulfillment);
                    if (order != null)
                    {
                        order.DiscountFraction = discountFraction;
                        if (fulfillment == FulfillmentMode.BuyerPickup && markReadyNow)
                        {
                            SalesOrderService.MarkReadyForPickup(order, fulfillmentMap);
                        }

                        // Deliberately stays on Find Buyer. Selling is usually several sales in
                        // a row — split a surplus across buyers, work down a list — and being
                        // thrown to Orders after each one interrupts exactly that.
                        //
                        // The Find Buyer action changed commitments, so invalidate immediately.
                        // The null cache bypasses the throttle when the player returns.
                        stockCache = null;
                        findBuyerCache = null;
                    }
                    return true;
                },
                (qty, fulfillment, discountFraction) =>
                    SalePaymentPreviewText(state, offer, qty, fulfillment, discountFraction),
                initialMarkReadyNow: IntercolonyMod.Settings.markReadyNowByDefault));
        }

        private void ConfirmAnimalSell(
            IntercolonyWorldComponent state, BuyerOffer offer, int deadlineDays)
        {
            Dialog_ConfirmQuantity sellDialog = null;
            sellDialog = new Dialog_ConfirmQuantity(
                "Sell animals to this buyer?",
                "Create order",
                offer.quantity,
                (qty, fulfillment, discountFraction) =>
                {
                    SalesOrder preview = BuildSalePaymentPreview(
                        state, offer, qty, fulfillment, discountFraction);
                    int waived = preview.TotalPayment - preview.DiscountedTotalPayment;
                    string tiles = offer.distanceTiles < 0f
                        ? "unknown"
                        : $"{offer.distanceTiles:F0}";
                    string paymentTip = $"{preview.unitPrice:F2} each before discount. Waived: " +
                        $"{waived} silver. The waived value improves your standing with the " +
                        "buyer's faction." +
                        (qty < offer.quantity
                            ? " A smaller lot earns a better rate per unit."
                            : "");
                    List<TermRow> rows = new List<TermRow>
                    {
                        new TermRow(null,
                            $"Sell {qty}x {offer.animalSpec.ShortLabel(offer.def)} to " +
                            $"{offer.settlement?.Label}"),
                        new TermRow("Payment", $"{preview.DiscountedTotalPayment} silver", paymentTip),
                        new TermRow("Distance",
                            offer.distanceTiles < 0f ? "unknown" : $"{tiles} tiles")
                    };

                    if (fulfillment == FulfillmentMode.BuyerPickup)
                    {
                        int pickupDays = SalesOrderService.EstimateBuyerPickupTravelDays(
                            offer.distanceTiles);
                        string distanceBasis = offer.distanceTiles < 0f
                            ? "an unknown distance"
                            : $"{tiles} tiles";
                        rows.Add(new TermRow("Fulfilment", "Buyer collects",
                            "No caravan is needed. When you mark the order ready you set aside " +
                            "the particular animals the buyer will collect, and the buyer takes " +
                            "those rather than any matching ones."));
                        rows.Add(new TermRow("Mark ready by", $"{deadlineDays} days from now",
                            "A fixed deadline to declare the animals ready. It does not depend on distance."));
                        rows.Add(new TermRow("Buyer arrives",
                            $"about {pickupDays} days after you mark ready",
                            $"Travel time from {offer.settlement?.Label}, estimated from {distanceBasis}."));
                    }
                    else
                    {
                        rows.Add(new TermRow("Fulfilment", "You deliver",
                            "Load matching animals into your caravan. The promise is by specification, " +
                            "so any animal meeting it will do."));
                        rows.Add(new TermRow("Deliver within", $"{deadlineDays} days"));
                    }

                    rows.Add(new TermRow("Commitment", "Binding",
                        "The quantity counts against what Find Buyer considers available, so it " +
                        "will not be offered to another buyer."));
                    rows.Add(new TermRow("Re-checked", "At handover",
                        "An animal that dies, is downed, goes feral or no longer matches will not " +
                        "be counted, and nothing is physically locked in the meantime."));
                    rows.Add(new TermRow("Bonded animals", "Confirmation if applicable",
                        "If any animal handed over is bonded you will be asked to confirm, and every " +
                        "affected colonist is named."));
                    return rows;
                },
                (qty, fulfillment, discountFraction, markReadyNow) =>
                {
                    BuyerOffer priced = offer;
                    priced.unitPrice = FindBuyerService.SellRateFor(state, offer, qty, fulfillment);
                    Map fulfillmentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                    SalesOrder pending = null;
                    if (fulfillment == FulfillmentMode.BuyerPickup && markReadyNow)
                    {
                        pending = SalesOrderService.BuildOrderFromOffer(
                            priced, qty, deadlineDays, fulfillment, fulfillmentMap);
                        if (!SalesOrderService.CanMarkReadyNow(
                                pending, fulfillmentMap, out string reason))
                        {
                            Messages.Message(
                                $"Order not created: {reason}\n" +
                                "Untick \"Mark ready now\" to create the order and ready it later.",
                                MessageTypeDefOf.RejectInput, historical: false);
                            return false;
                        }
                    }

                    Func<bool> createOrder = () =>
                    {
                        SalesOrder order = SalesOrderService.CreateFromOffer(
                            state, fulfillmentMap, priced, qty, deadlineDays, fulfillment);
                        if (order == null)
                        {
                            return false;
                        }

                        order.DiscountFraction = discountFraction;
                        if (fulfillment == FulfillmentMode.BuyerPickup && markReadyNow)
                        {
                            SalesOrderService.MarkReadyForPickup(order, fulfillmentMap);
                        }
                        // Stays put for the same reason as the goods sale above.
                        animalStockCache = null;
                        findBuyerCache = null;
                        return true;
                    };

                    if (pending != null)
                    {
                        string bondWarning =
                            SalesOrderService.BuildBondedAnimalWarning(pending, fulfillmentMap);
                        if (!bondWarning.NullOrEmpty())
                        {
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                                bondWarning,
                                () =>
                                {
                                    SalesOrder currentPending = SalesOrderService.BuildOrderFromOffer(
                                        priced, qty, deadlineDays, fulfillment, fulfillmentMap);
                                    if (!SalesOrderService.CanMarkReadyNow(
                                            currentPending, fulfillmentMap, out string reason))
                                    {
                                        Messages.Message(
                                            $"Order not created: {reason}\n" +
                                            "Untick \"Mark ready now\" to create the order and ready it later.",
                                            MessageTypeDefOf.RejectInput, historical: false);
                                        return;
                                    }
                                    if (createOrder())
                                    {
                                        sellDialog.Close();
                                    }
                                },
                                destructive: true));
                            return false;
                        }
                    }

                    createOrder();
                    return true;
                },
                (qty, fulfillment, discountFraction) =>
                    SalePaymentPreviewText(state, offer, qty, fulfillment, discountFraction),
                initialMarkReadyNow: IntercolonyMod.Settings.markReadyNowByDefault);
            Find.WindowStack.Add(sellDialog);
        }

        private static SalesOrder BuildSalePaymentPreview(
            IntercolonyWorldComponent state,
            BuyerOffer offer, int quantity, FulfillmentMode fulfillment,
            float discountFraction)
        {
            return new SalesOrder
            {
                line = new OrderLine(offer.def, quantity),
                unitPrice = FindBuyerService.SellRateFor(state, offer, quantity, fulfillment),
                fulfillment = fulfillment,
                DiscountFraction = discountFraction
            };
        }

        private static string SalePaymentPreviewText(
            IntercolonyWorldComponent state,
            BuyerOffer offer, int quantity, FulfillmentMode fulfillment,
            float discountFraction)
        {
            SalesOrder preview = BuildSalePaymentPreview(
                state, offer, quantity, fulfillment, discountFraction);
            int waived = preview.TotalPayment - preview.DiscountedTotalPayment;
            return $"Paid: {preview.DiscountedTotalPayment:N0} silver\n" +
                   $"Waived: {waived:N0} silver";
        }

        private Vector2 procurementScroll;
        private Vector2 procurementOrdersScroll;
        private const float PurchaseOrderHeaderHeight = 30f;
        private const float PurchaseOrderSectionHeaderHeight = 32f;
        private const float PurchaseOrderSectionGap = 8f;
        private const float PurchaseOrderMinimumRowHeight = 42f;

        /// <summary>
        /// Procurement (DESIGN.md §19, §55, §103). Requests with their quotes underneath, so
        /// comparing suppliers is a matter of reading down a list rather than clicking through.
        /// </summary>
        /// <summary>
        /// A page that exists so the shape of procurement matches the shape of selling, and
        /// says outright that it is not built yet. Better than omitting the tab, which would
        /// leave the two halves of the same business looking arbitrarily different.
        /// </summary>
        private void DrawPlaceholderPage(
            Rect inRect, Tab which, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 34f), TabLabel(which, state));
            Text.Font = GameFont.Small;
            y += 40f;

            string body = which == Tab.SupplierMarket
                ? "Under development.\n\nToday you ask settlements for what you need and they " +
                  "answer. A supplier market would work the other way around: standing offers " +
                  "you can browse and take, the way the selling Market already works."
                : "Under development.\n\nA procurement contract would be a standing agreement " +
                  "to buy — the mirror of the supply agreements you already offer, with a " +
                  "settlement committing to deliver on a cadence rather than one order at a time.";

            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, Mathf.Min(inRect.width, 560f), inRect.height - y), body);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Purchases already placed. The procurement mirror of the Sales Orders page; request
        /// history is deliberately not rendered here because a request and its order are distinct.
        /// </summary>
        private void DrawProcurementOrders(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            DrawMeasuredPurchaseOrderLabel(
                new Rect(0f, y, inRect.width, 34f), "Purchase orders");
            Text.Font = GameFont.Small;
            y += 40f;

            List<PurchaseOrdersRow> rows = PurchaseOrdersUiService.BuildRows(state);
            string emptyState = PurchaseOrdersUiService.EmptyState(rows);
            if (rows.Count == 0)
            {
                GUI.color = Color.gray;
                DrawMeasuredPurchaseOrderLabel(
                    new Rect(0f, y, inRect.width - 16f,
                        Text.CalcHeight(emptyState, Mathf.Max(1f, inRect.width - 16f))),
                    emptyState);
                GUI.color = Color.white;
                return;
            }

            PurchaseOrdersUiService.SortRows(
                rows, purchaseOrdersSortColumn, purchaseOrdersSortDescending);

            float tableWidth = Mathf.Max(1f, inRect.width - 16f);
            float emptyStateHeight = emptyState.NullOrEmpty()
                ? 0f
                : Text.CalcHeight(emptyState, tableWidth) + 6f;
            float contentHeight = emptyStateHeight + PurchaseOrderHeaderHeight +
                                  PurchaseOrdersContentHeight(rows, tableWidth);

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            Rect viewRect = new Rect(
                0f, 0f, inRect.width - 16f, Mathf.Max(contentHeight, outRect.height));

            BeginPageScrollView(outRect, ref procurementOrdersScroll, viewRect);
            float rowY = 0f;
            if (!emptyState.NullOrEmpty())
            {
                GUI.color = Color.gray;
                DrawMeasuredPurchaseOrderLabel(
                    new Rect(0f, rowY, tableWidth, emptyStateHeight - 6f), emptyState);
                GUI.color = Color.white;
                rowY += emptyStateHeight;
            }

            DrawPurchaseOrdersHeader(new Rect(0f, rowY, tableWidth, PurchaseOrderHeaderHeight));
            rowY += PurchaseOrderHeaderHeight;
            DrawPurchaseOrderSections(viewRect.width, rowY, rows, state, tableWidth);

            EndPageScrollView();
        }

        /// <summary>Draws the Supplier Market browse surface beside Find seller.</summary>
        private void DrawSupplierMarket(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;
            Text.Font = GameFont.Medium;
            DrawMeasuredSupplierLabel(new Rect(0f, y, inRect.width, 34f), "Supplier market");
            Text.Font = GameFont.Small;
            y += 40f;

            float tableWidth = Mathf.Max(1f, inRect.width - 16f);
            List<SupplierMarketRow> rows = GetSupplierMarketRows(state, tableWidth);
            if (rows.Count == 0)
            {
                GUI.color = Color.gray;
                string emptyMessage = SupplierMarketUiService.EmptyState(state);
                float emptyHeight = Text.CalcHeight(emptyMessage, inRect.width - 12f);
                DrawMeasuredSupplierLabel(
                    new Rect(0f, y, inRect.width - 12f, emptyHeight), emptyMessage);
                GUI.color = Color.white;
                return;
            }

            DrawSupplierMarketHeader(new Rect(0f, y, tableWidth, 28f));
            y += 28f;
            Widgets.DrawLineHorizontal(0f, y, inRect.width);
            y += 2f;

            float contentHeight = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                contentHeight += supplierMarketRowHeights[i];
            }

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            Rect viewRect = new Rect(0f, 0f, tableWidth, contentHeight);
            BeginPageScrollView(outRect, ref supplierMarketScroll, viewRect);

            float rowY = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                float rowHeight = supplierMarketRowHeights[i];
                DrawSupplierMarketRow(
                    new Rect(0f, rowY, tableWidth, rowHeight), rows[i], i, state);
                rowY += rowHeight;
            }

            EndPageScrollView();
        }

        private void ResetSupplierMarketCache()
        {
            supplierMarketRows = null;
            supplierMarketRowHeights = null;
            supplierMarketRowHeightsWidth = -1f;
            supplierMarketRowsListingCount = -1;
            supplierMarketRowsBuiltAtRealtime = 0f;
            supplierMarketRowsHaveSort = false;
        }

        private List<SupplierMarketRow> GetSupplierMarketRows(
            IntercolonyWorldComponent state, float tableWidth)
        {
            int listingCount = state?.SupplierListings?.Count ?? 0;
            float now = Time.realtimeSinceStartup;
            bool rebuild = supplierMarketRows == null ||
                           supplierMarketRowsListingCount != listingCount ||
                           now - supplierMarketRowsBuiltAtRealtime >
                           SupplierMarketRefreshIntervalSeconds;

            if (rebuild)
            {
                supplierMarketRows = SupplierMarketUiService.BuildRows(state);
                supplierMarketRowHeights = null;
                supplierMarketRowHeightsWidth = -1f;
                supplierMarketRowsListingCount = listingCount;
                supplierMarketRowsBuiltAtRealtime = now;
                supplierMarketRowsHaveSort = false;
            }

            if (!supplierMarketRowsHaveSort ||
                supplierMarketRowsSortColumn != supplierMarketSortColumn ||
                supplierMarketRowsSortDescending != supplierMarketSortDescending)
            {
                SupplierMarketUiService.SortRows(
                    supplierMarketRows, supplierMarketSortColumn, supplierMarketSortDescending);
                supplierMarketRowHeights = null;
                supplierMarketRowHeightsWidth = -1f;
                supplierMarketRowsSortColumn = supplierMarketSortColumn;
                supplierMarketRowsSortDescending = supplierMarketSortDescending;
                supplierMarketRowsHaveSort = true;
            }

            if (supplierMarketRowHeights == null ||
                supplierMarketRowHeights.Count != supplierMarketRows.Count ||
                supplierMarketRowHeightsWidth != tableWidth)
            {
                supplierMarketRowHeights = new List<float>(supplierMarketRows.Count);
                for (int i = 0; i < supplierMarketRows.Count; i++)
                {
                    supplierMarketRowHeights.Add(
                        SupplierMarketRowHeight(supplierMarketRows[i], tableWidth));
                }

                supplierMarketRowHeightsWidth = tableWidth;
            }

            return supplierMarketRows;
        }

        private void DrawSupplierMarketHeader(Rect rect)
        {
            float x = rect.x;
            for (int i = 0; i < SupplierMarketUiService.ColumnLabels.Length; i++)
            {
                float width = rect.width * SupplierMarketUiService.ColumnWidths[i];
                Rect cell = new Rect(x, rect.y, Mathf.Max(1f, width - 4f), rect.height);
                if (i == SupplierMarketUiService.ColumnLabels.Length - 1)
                {
                    x += width;
                    continue;
                }

                SupplierMarketColumn column = (SupplierMarketColumn)i;
                bool active = supplierMarketSortColumn == column;
                string label = SupplierMarketUiService.HeaderLabel(
                    column, active, supplierMarketSortDescending);
                Widgets.DrawHighlightIfMouseover(cell);
                GUI.color = active ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                DrawMeasuredSupplierLabel(cell, label);
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(cell))
                {
                    if (active)
                    {
                        supplierMarketSortDescending = !supplierMarketSortDescending;
                    }
                    else
                    {
                        supplierMarketSortColumn = column;
                        supplierMarketSortDescending =
                            SupplierMarketUiService.DefaultDescending(column);
                    }

                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }

                x += width;
            }
        }

        private void DrawSupplierMarketRow(
            Rect rect,
            SupplierMarketRow row,
            int index,
            IntercolonyWorldComponent state)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);
            if (ShouldBuildTooltip(rect))
            {
                string tooltip = SupplierMarketUiService.BuildTooltip(state, row);
                if (!tooltip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(rect, tooltip);
                }
            }

            for (int i = 0; i < (int)SupplierMarketColumn.Reason + 1; i++)
            {
                SupplierMarketColumn column = (SupplierMarketColumn)i;
                DrawMeasuredSupplierLabel(
                    SupplierMarketCell(rect, column),
                    SupplierMarketUiService.CellLabel(row, column));
            }

            Rect actionCell = SupplierMarketActionCell(rect);
            Rect buyRect = new Rect(
                actionCell.x,
                rect.y + Mathf.Max(0f, (rect.height - 26f) / 2f),
                actionCell.width,
                26f);
            if (Widgets.ButtonText(buyRect, "Buy", active: row.canBuy))
            {
                OpenSupplierMarketPurchase(state, row.listing);
            }

            if (!row.canBuy && !row.purchaseFailureReason.NullOrEmpty())
            {
                TooltipHandler.TipRegion(buyRect, row.purchaseFailureReason);
            }
        }

        private void OpenSupplierMarketPurchase(
            IntercolonyWorldComponent state, SupplierListing listing)
        {
            if (state == null || listing == null || !listing.IsAvailable)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_ConfirmQuantity(
                "Confirm purchase?",
                "Buy",
                listing.quantityAvailable,
                quantity => SupplierMarketUiService.BuildConfirmationRows(
                    state, listing, quantity),
                quantity =>
                {
                    bool purchased = SupplierListingService.TryPurchase(
                        state, listing, quantity, out _, out string failureReason);
                    if (purchased)
                    {
                        ResetSupplierMarketCache();
                    }
                    else if (!failureReason.NullOrEmpty())
                    {
                        // The purchase service owns this explanation. The UI does not compose a
                        // parallel reason that could drift from the transaction boundary.
                        Messages.Message(
                            failureReason, MessageTypeDefOf.RejectInput, historical: false);
                    }
                }));
        }

        private static float SupplierMarketRowHeight(SupplierMarketRow row, float tableWidth)
        {
            float height = 30f;
            for (int i = 0; i <= (int)SupplierMarketColumn.Reason; i++)
            {
                SupplierMarketColumn column = (SupplierMarketColumn)i;
                float cellWidth = tableWidth * SupplierMarketUiService.ColumnWidths[i] - 4f;
                height = Mathf.Max(
                    height,
                    Text.CalcHeight(
                        SupplierMarketUiService.CellLabel(row, column), Mathf.Max(1f, cellWidth)) +
                    8f);
            }

            return height;
        }

        private static Rect SupplierMarketCell(Rect row, SupplierMarketColumn column)
        {
            int index = (int)column;
            float x = row.x;
            for (int i = 0; i < index; i++)
            {
                x += row.width * SupplierMarketUiService.ColumnWidths[i];
            }

            return new Rect(
                x, row.y + 4f, Mathf.Max(
                    1f, row.width * SupplierMarketUiService.ColumnWidths[index] - 4f),
                Mathf.Max(1f, row.height - 8f));
        }

        private static Rect SupplierMarketActionCell(Rect row)
        {
            float x = row.x;
            for (int i = 0; i < SupplierMarketUiService.ColumnLabels.Length - 1; i++)
            {
                x += row.width * SupplierMarketUiService.ColumnWidths[i];
            }

            return new Rect(
                x, row.y + 4f,
                Mathf.Max(1f, row.width * SupplierMarketUiService.ColumnWidths[
                    SupplierMarketUiService.ColumnLabels.Length - 1] - 4f),
                Mathf.Max(1f, row.height - 8f));
        }

        private static void DrawMeasuredSupplierLabel(Rect rect, string text)
        {
            string value = text ?? "";
            float measuredHeight = Text.CalcHeight(value, Mathf.Max(1f, rect.width));
            Widgets.Label(
                new Rect(rect.x, rect.y, rect.width, Mathf.Max(rect.height, measuredHeight)), value);
        }

        private void DrawFindSeller(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width - 200f, 34f), "Find seller");
            Text.Font = GameFont.Small;

            Rect newRect = new Rect(inRect.width - 190f, y + 2f, 180f, 30f);
            if (Widgets.ButtonText(newRect, "Request goods..."))
            {
                Find.WindowStack.Add(new Dialog_CreateRequest(state));
            }

            y += 40f;

            // Only live requests. Find seller is where a purchase is decided, and a concluded
            // request is not a decision waiting to be made. Purchase-order rows are kept on the
            // separate Purchase Orders surface rather than being repeated alongside request rows.
            List<PurchaseRequest> requests = new List<PurchaseRequest>();
            foreach (PurchaseRequest request in state.Requests)
            {
                if (request != null && request.IsOpen)
                {
                    requests.Add(request);
                }
            }

            requests.Sort((a, b) => b.id.CompareTo(a.id));

            float contentHeight = 0f;
            foreach (PurchaseRequest request in requests)
            {
                contentHeight += RequestBlockHeight(request);
            }

            if (requests.Count == 0)
            {
                string emptyMessage = "No requests out.\n\n" +
                                      "Intercolony is not a shop. You state what you need, and known settlements " +
                                      "answer if they can — sometimes with less than you asked for, sometimes not " +
                                      "at all. Requests you have already acted on are no longer " +
                                      "waiting decisions here.";
                contentHeight += Text.CalcHeight(emptyMessage, inRect.width - 16f);
            }

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            Rect viewRect = new Rect(
                0f, 0f, inRect.width - 16f, Mathf.Max(contentHeight, outRect.height));

            BeginPageScrollView(outRect, ref procurementScroll, viewRect);
            float rowY = 0f;
            if (requests.Count == 0)
            {
                GUI.color = Color.gray;
                string emptyMessage = "No requests out.\n\n" +
                                      "Intercolony is not a shop. You state what you need, and known settlements " +
                                      "answer if they can — sometimes with less than you asked for, sometimes not " +
                                      "at all. Requests you have already acted on are no longer " +
                                      "waiting decisions here.";
                Widgets.Label(new Rect(0f, rowY, viewRect.width, Text.CalcHeight(emptyMessage, viewRect.width)), emptyMessage);
                GUI.color = Color.white;
            }

            foreach (PurchaseRequest request in requests)
            {
                float height = RequestBlockHeight(request);
                DrawRequestBlock(new Rect(0f, rowY, viewRect.width, height), request, state);
                rowY += height;
            }

            EndPageScrollView();
        }

        private static float PurchaseOrdersContentHeight(
            List<PurchaseOrdersRow> rows, float tableWidth)
        {
            bool hasLive = false;
            bool hasConcluded = false;
            float height = 0f;
            foreach (PurchaseOrdersRow row in rows)
            {
                if (row.isLive)
                {
                    hasLive = true;
                }
                else
                {
                    hasConcluded = true;
                }
            }

            if (hasLive)
            {
                height += PurchaseOrderSectionHeaderHeight;
                foreach (PurchaseOrdersRow row in rows)
                {
                    if (row.isLive)
                    {
                        height += PurchaseOrderRowHeight(row, tableWidth);
                    }
                }
            }

            if (hasConcluded)
            {
                height += (hasLive ? PurchaseOrderSectionGap : 0f) +
                          PurchaseOrderSectionHeaderHeight;
                foreach (PurchaseOrdersRow row in rows)
                {
                    if (!row.isLive)
                    {
                        height += PurchaseOrderRowHeight(row, tableWidth);
                    }
                }
            }

            return height;
        }

        private void DrawPurchaseOrdersHeader(Rect rect)
        {
            float x = rect.x;
            for (int i = 0; i < PurchaseOrdersUiService.ColumnLabels.Length; i++)
            {
                PurchaseOrdersColumn column = (PurchaseOrdersColumn)i;
                float width = rect.width * PurchaseOrdersUiService.ColumnWidths[i];
                Rect cell = new Rect(x, rect.y, Mathf.Max(1f, width - 4f), rect.height);
                if (column == PurchaseOrdersColumn.Action)
                {
                    x += width;
                    continue;
                }

                bool active = purchaseOrdersSortColumn == column;
                string label = PurchaseOrdersUiService.HeaderLabel(
                    column, active, purchaseOrdersSortDescending);
                Widgets.DrawHighlightIfMouseover(cell);
                GUI.color = active ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                if (column == PurchaseOrdersColumn.Quantity ||
                    column == PurchaseOrdersColumn.TotalPrice)
                {
                    Text.Anchor = TextAnchor.UpperRight;
                }
                DrawMeasuredPurchaseOrderLabel(cell, label);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(cell))
                {
                    if (active)
                    {
                        purchaseOrdersSortDescending = !purchaseOrdersSortDescending;
                    }
                    else
                    {
                        purchaseOrdersSortColumn = column;
                        purchaseOrdersSortDescending =
                            PurchaseOrdersUiService.DefaultDescending(column);
                    }

                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }

                x += width;
            }
        }

        private float DrawPurchaseOrderSections(
            float width,
            float y,
            List<PurchaseOrdersRow> rows,
            IntercolonyWorldComponent state,
            float tableWidth)
        {
            int liveCount = 0;
            foreach (PurchaseOrdersRow row in rows)
            {
                if (row.isLive)
                {
                    liveCount++;
                }
            }

            if (liveCount > 0)
            {
                string header = $"Live orders ({liveCount})";
                DrawMeasuredPurchaseOrderLabel(
                    new Rect(0f, y, width, PurchaseOrderSectionHeaderHeight), header);
                y += PurchaseOrderSectionHeaderHeight;
                int index = 0;
                foreach (PurchaseOrdersRow row in rows)
                {
                    if (!row.isLive)
                    {
                        continue;
                    }

                    float rowHeight = PurchaseOrderRowHeight(row, tableWidth);
                    DrawPurchaseOrderRow(
                        new Rect(0f, y, tableWidth, rowHeight), row, index++);
                    y += rowHeight;
                }
            }

            int concludedCount = rows.Count - liveCount;
            if (concludedCount > 0)
            {
                y += liveCount > 0 ? PurchaseOrderSectionGap : 0f;
                const float buttonWidth = 190f;
                int clearableCount =
                    OrderHistoryService.CountClearablePurchaseOrderHistory(state);
                float labelWidth = clearableCount > 0
                    ? width - buttonWidth - 8f
                    : width;
                string header = $"Concluded orders ({concludedCount})";
                DrawMeasuredPurchaseOrderLabel(
                    new Rect(0f, y, labelWidth, PurchaseOrderSectionHeaderHeight), header);
                if (clearableCount > 0)
                {
                    Rect clearRect = new Rect(
                        width - buttonWidth, y + 2f, buttonWidth, 26f);
                    if (Widgets.ButtonText(clearRect, "Clear completed history"))
                    {
                        string orderWord = clearableCount == 1 ? "order" : "orders";
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            $"Remove {clearableCount} concluded {orderWord} from this list?\n\n" +
                            "Live purchase orders and your trading record will be kept.",
                            () => OrderHistoryService.ClearPurchaseOrderHistory(state),
                            destructive: true));
                    }
                }

                y += PurchaseOrderSectionHeaderHeight;
                int index = 0;
                foreach (PurchaseOrdersRow row in rows)
                {
                    if (row.isLive)
                    {
                        continue;
                    }

                    float rowHeight = PurchaseOrderRowHeight(row, tableWidth);
                    DrawPurchaseOrderRow(
                        new Rect(0f, y, tableWidth, rowHeight), row, index++);
                    y += rowHeight;
                }
            }

            return y;
        }

        private static float PurchaseOrderRowHeight(PurchaseOrdersRow row, float tableWidth)
        {
            float height = PurchaseOrderMinimumRowHeight;
            for (int i = 0; i < (int)PurchaseOrdersColumn.Action; i++)
            {
                PurchaseOrdersColumn column = (PurchaseOrdersColumn)i;
                float cellWidth = tableWidth * PurchaseOrdersUiService.ColumnWidths[i] - 8f;
                height = Mathf.Max(
                    height,
                    Text.CalcHeight(
                        PurchaseOrdersUiService.CellLabel(row, column), Mathf.Max(1f, cellWidth)) +
                    8f);
            }

            return height;
        }

        private void DrawPurchaseOrderRow(
            Rect rect, PurchaseOrdersRow row, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);
            if (ShouldBuildTooltip(rect) && !row.tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, row.tooltip);
            }

            GUI.color = row.isLive ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            for (int i = 0; i < (int)PurchaseOrdersColumn.Action; i++)
            {
                PurchaseOrdersColumn column = (PurchaseOrdersColumn)i;
                Rect cell = PurchaseOrderCell(rect, column);
                if (column == PurchaseOrdersColumn.Quantity ||
                    column == PurchaseOrdersColumn.TotalPrice)
                {
                    Text.Anchor = TextAnchor.UpperRight;
                }
                DrawMeasuredPurchaseOrderLabel(
                    cell, PurchaseOrdersUiService.CellLabel(row, column));
                Text.Anchor = TextAnchor.UpperLeft;
            }

            GUI.color = Color.white;
            if (row.canCancel)
            {
                Rect actionCell = PurchaseOrderCell(rect, PurchaseOrdersColumn.Action);
                Rect cancelRect = new Rect(
                    actionCell.x,
                    rect.y + Mathf.Max(0f, (rect.height - 26f) / 2f),
                    actionCell.width,
                    26f);
                if (Widgets.ButtonText(cancelRect, row.actionLabel))
                {
                    ConfirmPurchaseCancellation(row);
                }
            }
        }

        private static Rect PurchaseOrderCell(Rect row, PurchaseOrdersColumn column)
        {
            int index = (int)column;
            float x = row.x;
            for (int i = 0; i < index; i++)
            {
                x += row.width * PurchaseOrdersUiService.ColumnWidths[i];
            }

            return new Rect(
                x + 4f,
                row.y + 4f,
                Mathf.Max(1f, row.width * PurchaseOrdersUiService.ColumnWidths[index] - 8f),
                Mathf.Max(1f, row.height - 8f));
        }

        private static void DrawMeasuredPurchaseOrderLabel(Rect rect, string text)
        {
            string value = text ?? "";
            float measuredHeight = Text.CalcHeight(value, Mathf.Max(1f, rect.width));
            Widgets.Label(
                new Rect(rect.x, rect.y, rect.width, Mathf.Max(rect.height, measuredHeight)), value);
        }

        /// <summary>Compatibility seam for existing order diagnostics.</summary>
        internal static List<PurchaseOrder> SelectPurchaseOrdersForDisplay(
            IEnumerable<PurchaseOrder> orders)
        {
            return PurchaseOrdersUiService.SelectPurchaseOrdersForDisplay(orders);
        }

        private void ConfirmPurchaseCancellation(PurchaseOrdersRow row)
        {
            PurchaseOrder order = row.order;
            if (order == null || !row.canCancel)
            {
                return;
            }

            Action cancelPurchase = () =>
            {
                if (!PurchaseOrderService.Cancel(order, out string refusalReason) &&
                    !refusalReason.NullOrEmpty())
                {
                    Messages.Message(
                        refusalReason, MessageTypeDefOf.RejectInput, historical: false);
                }
            };
            Find.WindowStack.Add(new Dialog_MessageBox(
                $"Purchase #{order.id}: {order.quantity}x {order.ItemLabel()} from " +
                $"{order.settlementName}.\n\n" +
                $"You already paid {order.paidSilver} silver. All of it will be forfeited; " +
                "none will be refunded. The goods will not arrive. " +
                $"The cancellation will be recorded in your trading record with {order.settlementName}.",
                "Cancel purchase",
                cancelPurchase,
                "Keep order",
                () => { },
                "Cancel purchase order",
                buttonADestructive: true,
                acceptAction: cancelPurchase,
                cancelAction: () => { }));
        }

        private Vector2 contractsScroll;
        private List<Settlement> contractProposalSettlementCache;

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

            bool receiveProposals = state.ReceiveContractProposals;
            Widgets.CheckboxLabeled(
                new Rect(0f, y, Mathf.Min(280f, inRect.width), 28f),
                "Receive contract proposals", ref receiveProposals);
            state.ReceiveContractProposals = receiveProposals;

            if (contractProposalSettlementCache == null)
            {
                contractProposalSettlementCache = EligibleContractProposalSettlements(state);
            }

            Rect proposeRect = new Rect(inRect.width - 190f, y, 190f, 28f);
            if (Widgets.ButtonText(
                    proposeRect, "Propose supply agreement",
                    active: contractProposalSettlementCache.Count > 0))
            {
                Find.WindowStack.Add(new Dialog_ProposeAgreement(state));
            }

            if (contractProposalSettlementCache.Count == 0)
            {
                TooltipHandler.TipRegion(
                    proposeRect,
                    "No settlement currently qualifies for a supply agreement based on your trading record.");
            }

            y += 28f;

            if (receiveProposals)
            {
                float categoryWidth = (inRect.width - 16f) / 3f;
                for (int i = 0; i < IntercolonyProductCategoryUtility.All.Length; i++)
                {
                    IntercolonyProductCategory category = IntercolonyProductCategoryUtility.All[i];
                    bool enabled = state.ReceiveContractProposalsFor(category);
                    float x = (i % 3) * (categoryWidth + 8f);
                    float categoryY = y + (i / 3) * 28f;
                    Widgets.CheckboxLabeled(
                        new Rect(x, categoryY, categoryWidth, 28f), category.Label(), ref enabled);
                    state.SetReceiveContractProposalsFor(category, enabled);
                }

                y += 56f;
            }

            List<RecurringContract> contracts = new List<RecurringContract>(state.Contracts);
            if (contracts.Count == 0)
            {
                GUI.color = Color.gray;
                string emptyMessage = "No supply agreements.\n\n" +
                                      "Settlements that trust you may offer standing agreements — a fixed quantity " +
                                      "every quadrum for a fixed term, at better than spot prices. Build a trading " +
                                      "record first: a settlement will not stake a year of supply on a stranger.";
                Widgets.Label(new Rect(0f, y, inRect.width, Text.CalcHeight(emptyMessage, inRect.width)), emptyMessage);
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

            BeginPageScrollView(outRect, ref contractsScroll, viewRect);
            float rowY = 0f;
            for (int i = 0; i < contracts.Count; i++)
            {
                DrawContractRow(new Rect(0f, rowY, viewRect.width, 74f), contracts[i], i, state);
                rowY += 74f;
            }

            EndPageScrollView();
        }

        private static List<Settlement> EligibleContractProposalSettlements(
            IntercolonyWorldComponent state)
        {
            List<Settlement> result = new List<Settlement>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return result;
            }

            foreach (Settlement settlement in settlements)
            {
                if (EligibleContractProposalItems(state, settlement).Count > 0)
                {
                    result.Add(settlement);
                }
            }

            result.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static List<ThingDef> EligibleContractProposalItems(
            IntercolonyWorldComponent state, Settlement settlement)
        {
            List<ThingDef> result = new List<ThingDef>();
            HashSet<ThingDef> seen = new HashSet<ThingDef>();
            foreach (CommercialHistoryEntry entry in state.CommercialHistory)
            {
                ThingDef thingDef = entry?.thingDef;
                if (entry == null || entry.settlementId != settlement.ID || thingDef == null ||
                    !seen.Add(thingDef))
                {
                    continue;
                }

                if (ContractService.PreviewContractTerms(
                        state, settlement, thingDef,
                        ContractService.MinimumQuantityPerCycle) != null)
                {
                    result.Add(thingDef);
                }
            }

            result.Sort((a, b) => string.Compare(
                a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.OrdinalIgnoreCase));
            return result;
        }

        internal void InvalidateContractProposalSettlementCache()
        {
            contractProposalSettlementCache = null;
        }

        private static int ContractRank(RecurringContract contract)
        {
            if (contract.IsOffer || contract.IsPendingPlayerProposal) return 0;

            // A renewal waiting on an answer sorts to the top with new offers: it expires, so it is
            // the thing the player needs to see (§115).
            if (contract.renewalOffered) return 0;

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
            string paymentSummary =
                $"{contract.totalCycles} deliveries   " +
                $"{contract.DiscountedCyclePayment} silver each   " +
                $"{contract.DiscountedTotalPayment} total";
            if (contract.DiscountFraction > 0f)
            {
                paymentSummary +=
                    $"   {contract.unitPrice:F2} agreed rate; " +
                    $"{contract.DiscountFraction.ToStringPercent("F0")} waived";
            }

            Widgets.Label(new Rect(rect.x + 6f, rect.y + 26f, rect.width - 220f, 22f),
                paymentSummary);
            GUI.color = Color.white;

            string status;
            Color colour = Color.white;
            if (contract.IsPendingPlayerProposal)
            {
                status = "awaiting the settlement's answer";
                colour = new Color(0.6f, 0.9f, 1f);
            }
            else if (contract.IsOffer)
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
            else if (contract.renewalOffered)
            {
                status = $"they would sign again — {contract.DaysUntilRenewalExpires:F1}d to answer";
                colour = new Color(0.65f, 0.95f, 0.65f);
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
                status = contract.status.ToString();
                if (!string.IsNullOrEmpty(contract.outcomeNote))
                {
                    status += $": {contract.outcomeNote}";
                }
                colour = contract.status == ContractStatus.Completed
                    ? new Color(0.6f, 0.9f, 0.6f)
                    : new Color(0.9f, 0.6f, 0.6f);
            }

            GUI.color = colour;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 48f, rect.width - 220f, 22f),
                $"{contract.cyclesCompleted} delivered, {contract.cyclesFailed} missed — {status}");
            GUI.color = Color.white;

            if (ShouldBuildTooltip(rect))
            {
                string paymentTerms = contract.DiscountFraction > 0f
                    ? $"Payment: {contract.DiscountedCyclePayment} silver per delivery, " +
                      $"{contract.DiscountedTotalPayment} total.\n" +
                      $"Agreed rate: {contract.unitPrice:F2} silver each; " +
                      $"{contract.DiscountFraction.ToStringPercent("F0")} waived."
                    : $"Payment: {contract.DiscountedCyclePayment} silver per delivery, " +
                      $"{contract.DiscountedTotalPayment} total, at " +
                      $"{contract.unitPrice:F2} silver each.";

                TooltipHandler.TipRegion(rect,
                    $"{contract.settlementName} ({contract.factionName})\n\n" +
                    $"{contract.quantityPerCycle}x {contract.ItemLabel()} every " +
                    $"{contract.CadenceDays:F0} days, {contract.totalCycles} times.\n" +
                    paymentTerms + "\n" +
                    "The agreed rate is above spot because they are buying certainty.\n\n" +
                    "Each cycle raises a delivery order with the full cadence as its deadline. " +
                    $"Missing {RecurringContract.BreachThreshold} deliveries in a row ends the " +
                    "agreement and badly damages your standing.");
            }

            // A renewal offer is answered here rather than through a separate flow (§115): it is the
            // same agreement, on the same terms, and it expires if left alone.
            if (contract.renewalOffered)
            {
                Rect renewRect = new Rect(rect.xMax - 200f, rect.y + 20f, 92f, 30f);
                if (Widgets.ButtonText(renewRect, "Renew"))
                {
                    ContractService.AcceptRenewal(state, contract);
                }

                Rect declineRect = new Rect(rect.xMax - 100f, rect.y + 20f, 92f, 30f);
                if (Widgets.ButtonText(declineRect, "Decline"))
                {
                    ContractService.DeclineRenewal(contract);
                }
            }
            else if (contract.IsOffer)
            {
                Rect acceptRect = new Rect(rect.xMax - 200f, rect.y + 20f, 92f, 30f);
                if (Widgets.ButtonText(acceptRect, "Accept"))
                {
                    ConfirmContract(state, contract);
                }

                Rect declineRect = new Rect(rect.xMax - 100f, rect.y + 20f, 92f, 30f);
                if (Widgets.ButtonText(declineRect, "Decline"))
                {
                    contract.TryDecline("Declined by the player.");
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
            Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            Pawn negotiator = TransitionService.BestNegotiator(map);
            float negotiatedRate = ContractService.NegotiatedUnitPrice(contract, negotiator);
            int offeredQuantity = contract.quantityPerCycle;

            Find.WindowStack.Add(new Dialog_ConfirmQuantity(
                "Standing supply agreement",
                "Accept agreement",
                ContractService.MaxAcceptableQuantity(contract),
                (qty, fulfillment) =>
                {
                    int cycleValue = Mathf.RoundToInt(negotiatedRate * qty);
                    string logistics = fulfillment == FulfillmentMode.BuyerPickup
                        ? "They collect each delivery, so no caravan is needed — but the goods " +
                          "must be ready and marked so every cycle."
                        : "You deliver each cycle by caravan.";

                    string negotiation = negotiator == null
                        ? "No colonist was free to negotiate, so the rate is as offered."
                        : $"Negotiated by {negotiator.LabelShortCap} " +
                          $"(Social {negotiator.skills.GetSkill(SkillDefOf.Social).Level}): " +
                          $"{contract.unitPrice:F2} → {negotiatedRate:F2} each.";

                    string sizing = qty == offeredQuantity
                        ? "This is the size they asked for."
                        : qty > offeredQuantity
                            ? $"You are offering {qty - offeredQuantity} more per cycle than they asked for."
                            : $"You are committing to {offeredQuantity - qty} fewer per cycle than they asked for.";

                    return $"{contract.settlementName} wants {offeredQuantity}x " +
                           $"{contract.ItemLabel()} every {contract.CadenceDays:F0} days, " +
                           $"{contract.totalCycles} times.\n\n" +
                           $"{negotiation}\n\n" +
                           $"Payment: {cycleValue} silver per delivery, " +
                           $"{cycleValue * contract.totalCycles} in total\n" +
                           $"Rate: {negotiatedRate:F2} each — better than spot, because they are " +
                           "buying certainty\n\n" +
                           $"{sizing} That is roughly " +
                           $"{qty / Mathf.Max(1f, contract.CadenceDays):F1} units per day of " +
                           "sustained output. Make sure you can hold that pace: missing " +
                           $"{RecurringContract.BreachThreshold} deliveries in a row ends the " +
                           "agreement and badly damages your standing with them.\n\n" +
                           logistics;
                },
                (qty, fulfillment) =>
                    ContractService.AcceptOffer(state, contract, qty, fulfillment, negotiator),
                // A standing agreement runs for many cycles, so the caravan-free option is the
                // one to start on. The contract's own field keeps its old default so existing
                // agreements are unaffected; this is only what the popup opens showing.
                FulfillmentMode.BuyerPickup,
                ContractService.MinAcceptableQuantity(contract),
                "Per delivery:"));
        }

        private Vector2 relationsScroll;
        private const int NoExpandedRelation = -1;
        // §7.6 choice A: Relations already owns a bounded page scroll and the existing inline
        // row-selection pattern; modal windows in this UI are reserved for commitment workflows.
        private int expandedRelationSettlementId = NoExpandedRelation;

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

            List<CommercialHistoryRelationRow> records =
                CommercialHistoryUiService.BuildRows(state);

            if (records.Count == 0)
            {
                GUI.color = Color.gray;
                string emptyMessage = "No trading history yet.\n\n" +
                                      "Complete or fail an order and that settlement will form an opinion. " +
                                      "Reputation is held per settlement and is separate from faction goodwill — " +
                                      "being liked is not the same as being relied on.";
                Widgets.Label(new Rect(0f, y, inRect.width, Text.CalcHeight(emptyMessage, inRect.width)), emptyMessage);
                GUI.color = Color.white;
                return;
            }

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            float viewWidth = Mathf.Max(1f, inRect.width - 16f);
            float contentHeight = 0f;
            for (int i = 0; i < records.Count; i++)
            {
                contentHeight += RelationRowHeight(
                    records[i],
                    viewWidth,
                    records[i].settlementId == expandedRelationSettlementId);
            }

            Rect viewRect = new Rect(0f, 0f, viewWidth, contentHeight);

            BeginPageScrollView(outRect, ref relationsScroll, viewRect);
            float rowY = 0f;
            for (int i = 0; i < records.Count; i++)
            {
                bool expanded = records[i].settlementId == expandedRelationSettlementId;
                float rowHeight = RelationRowHeight(records[i], viewRect.width, expanded);
                DrawRelationRow(
                    new Rect(0f, rowY, viewRect.width, rowHeight),
                    records[i],
                    i,
                    expanded);
                rowY += rowHeight;
            }

            EndPageScrollView();
        }

        private void DrawRelationRow(
            Rect rect, CommercialHistoryRelationRow row, int index, bool expanded)
        {
            const float HeaderHeight = 58f;
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(headerRect);
            }

            Widgets.DrawHighlightIfMouseover(headerRect);
            if (expanded)
            {
                Widgets.DrawHighlightSelected(headerRect);
            }

            float nameWidth = rect.width * 0.34f;
            float scoreWidth = rect.width * 0.28f;
            float factionWidth = rect.width - nameWidth - scoreWidth - 6f;
            float nameHeight = Text.CalcHeight(row.settlementLabel, nameWidth - 6f);
            Widgets.Label(
                new Rect(rect.x + 6f, rect.y + 4f, nameWidth - 6f, nameHeight),
                row.settlementLabel);

            GUI.color = row.hasReputation ? TierColour(row.tier) : Color.gray;
            float scoreHeight = Text.CalcHeight(row.scoreLabel, scoreWidth - 6f);
            Widgets.Label(
                new Rect(rect.x + nameWidth, rect.y + 4f, scoreWidth - 6f, scoreHeight),
                row.scoreLabel);
            GUI.color = Color.white;

            // Owning faction and its goodwill beside it, per §27's illustrative UI — the
            // two numbers together are the point: liked is not the same as relied upon.
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            float factionHeight = Text.CalcHeight(row.factionAndGoodwillLabel, factionWidth);
            Widgets.Label(
                new Rect(rect.x + nameWidth + scoreWidth, rect.y + 4f, factionWidth, factionHeight),
                row.factionAndGoodwillLabel);
            GUI.color = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            float statsWidth = rect.width - 12f;
            float statsHeight = Text.CalcHeight(row.statsLabel, statsWidth);
            Widgets.Label(
                new Rect(rect.x + 6f, rect.y + 28f, statsWidth, statsHeight),
                row.statsLabel);
            GUI.color = Color.white;

            if (ShouldBuildTooltip(headerRect))
            {
                TooltipHandler.TipRegion(headerRect, row.rowTooltip);
            }

            if (Widgets.ButtonInvisible(headerRect))
            {
                expandedRelationSettlementId = expanded
                    ? NoExpandedRelation
                    : row.settlementId;
            }

            if (expanded)
            {
                DrawRelationHistoryDetail(
                    new Rect(rect.x, rect.y + HeaderHeight, rect.width, rect.height - HeaderHeight),
                    row);
            }
        }

        private static float RelationRowHeight(
            CommercialHistoryRelationRow row, float width, bool expanded)
        {
            return 58f + (expanded ? RelationHistoryDetailHeight(row, width) : 0f);
        }

        private static float RelationHistoryDetailHeight(
            CommercialHistoryRelationRow row, float width)
        {
            float contentWidth = Mathf.Max(1f, width - 12f);
            float labelWidth = Mathf.Min(190f, contentWidth * 0.42f);
            float valueWidth = Mathf.Max(1f, contentWidth - labelWidth - 12f);
            float y = 6f;

            string summaryHeading = "Commercial history";
            y += Text.CalcHeight(summaryHeading, contentWidth) + 4f;
            for (int i = 0; i < row.summaryRows.Count; i++)
            {
                CommercialHistorySummaryRow summary = row.summaryRows[i];
                float keyHeight = Text.CalcHeight(summary.label, labelWidth);
                float valueHeight = Text.CalcHeight(summary.value, valueWidth);
                y += Mathf.Max(keyHeight, valueHeight) + 4f;
            }

            string timelineHeading = "Recent activity";
            y += Text.CalcHeight(timelineHeading, contentWidth) + 4f;
            if (row.timelineRows.Count == 0)
            {
                y += Text.CalcHeight(row.emptyTimelineLabel, contentWidth) + 4f;
            }
            else
            {
                for (int i = 0; i < row.timelineRows.Count; i++)
                {
                    y += Text.CalcHeight(row.timelineRows[i].label, contentWidth) + 4f;
                }
            }

            return y + 6f;
        }

        private static void DrawRelationHistoryDetail(
            Rect rect, CommercialHistoryRelationRow row)
        {
            Widgets.DrawLightHighlight(rect);
            float contentWidth = Mathf.Max(1f, rect.width - 12f);
            float labelWidth = Mathf.Min(190f, contentWidth * 0.42f);
            float valueWidth = Mathf.Max(1f, contentWidth - labelWidth - 12f);
            float valueX = rect.x + labelWidth + 12f;
            float y = rect.y + 6f;

            string summaryHeading = "Commercial history";
            float headingHeight = Text.CalcHeight(summaryHeading, contentWidth);
            Widgets.Label(new Rect(rect.x + 6f, y, contentWidth, headingHeight), summaryHeading);
            y += headingHeight + 4f;

            for (int i = 0; i < row.summaryRows.Count; i++)
            {
                CommercialHistorySummaryRow summary = row.summaryRows[i];
                float keyHeight = Text.CalcHeight(summary.label, labelWidth);
                float valueHeight = Text.CalcHeight(summary.value, valueWidth);
                float rowHeight = Mathf.Max(keyHeight, valueHeight);
                Widgets.Label(new Rect(rect.x + 6f, y, labelWidth, keyHeight), summary.label);
                Rect valueRect = new Rect(valueX, y, valueWidth, valueHeight);
                Widgets.Label(valueRect, summary.value);
                if (!string.IsNullOrEmpty(summary.tooltip))
                {
                    TooltipHandler.TipRegion(valueRect, summary.tooltip);
                }

                y += rowHeight + 4f;
            }

            Widgets.DrawLineHorizontal(rect.x + 6f, y, contentWidth);
            y += 4f;
            string timelineHeading = "Recent activity";
            float timelineHeadingHeight = Text.CalcHeight(timelineHeading, contentWidth);
            Widgets.Label(
                new Rect(rect.x + 6f, y, contentWidth, timelineHeadingHeight),
                timelineHeading);
            y += timelineHeadingHeight + 4f;

            if (row.timelineRows.Count == 0)
            {
                float emptyHeight = Text.CalcHeight(row.emptyTimelineLabel, contentWidth);
                GUI.color = Color.gray;
                Widgets.Label(
                    new Rect(rect.x + 6f, y, contentWidth, emptyHeight),
                    row.emptyTimelineLabel);
                GUI.color = Color.white;
                return;
            }

            for (int i = 0; i < row.timelineRows.Count; i++)
            {
                CommercialHistoryTimelineRow timeline = row.timelineRows[i];
                float eventHeight = Text.CalcHeight(timeline.label, contentWidth);
                Rect eventRect = new Rect(rect.x + 6f, y, contentWidth, eventHeight);
                Widgets.Label(eventRect, timeline.label);
                if (!string.IsNullOrEmpty(timeline.tooltip))
                {
                    TooltipHandler.TipRegion(eventRect, timeline.tooltip);
                }

                y += eventHeight + 4f;
            }
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

        private const float RequestSummaryHeight = 46f;
        private const float QuoteHeaderHeight = 24f;
        private const float RequestHeaderHeight = RequestSummaryHeight + QuoteHeaderHeight;
        private const float QuoteRowHeight = 26f;

        private static readonly float[] QuoteColumnWidths =
            { 0.23f, 0.12f, 0.11f, 0.12f, 0.10f, 0.13f, 0.08f, 0.11f };

        private static readonly string[] QuoteColumnLabels =
            { "Supplier", "Offered", "Unit", "Total", "Lead", "Terms", "Dist", "" };

        private static float RequestBlockHeight(PurchaseRequest request)
        {
            if (!request.IsOpen)
            {
                return RequestSummaryHeight + 10f;
            }

            int rows = Mathf.Max(1, request.quotes.Count);
            return RequestHeaderHeight + rows * QuoteRowHeight + 10f;
        }

        private void DrawRequestBlock(Rect rect, PurchaseRequest request, IntercolonyWorldComponent state)
        {
            Widgets.DrawLightHighlight(new Rect(rect.x, rect.y, rect.width, RequestSummaryHeight));

            string quantityLabel = request.quantityOrdered > 0 && request.IsOpen
                ? $"{request.QuantityOutstanding}x {request.ItemLabel()} still wanted " +
                  $"({request.quantityOrdered} of {request.quantityRequested} ordered)"
                : $"{request.quantityRequested}x {request.ItemLabel()}";
            string header = $"#{request.id}  {quantityLabel}";
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 200f, 22f), header);

            string sub;
            Color colour = Color.white;
            if (request.IsOpen)
            {
                sub = request.AnyQuotes
                    ? $"{request.quotes.Count} quote(s) — offers stand for {request.DaysRemaining:F1}d — " +
                      RequestFulfillmentLabel(request.fulfillmentPreference)
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
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Withdraw purchase request #{request.id}?\n\n" +
                        "Its quotations will be discarded. Withdrawing costs no silver.",
                        () =>
                        {
                            if (request.TryCancel())
                            {
                                Messages.Message(
                                    $"Purchase request #{request.id} withdrawn.",
                                    MessageTypeDefOf.NeutralEvent, historical: true);
                            }
                        },
                        destructive: false,
                        title: "Withdraw purchase request?"));
                }
            }

            if (!request.IsOpen)
            {
                return;
            }

            Rect quoteArea = new Rect(
                rect.x + 16f, rect.y + RequestSummaryHeight, rect.width - 24f, QuoteHeaderHeight);
            DrawQuoteHeader(quoteArea);

            float rowY = rect.y + RequestHeaderHeight;
            if (request.quotes.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Widgets.Label(new Rect(rect.x + 20f, rowY + 2f, rect.width - 40f, 22f),
                    "— nothing available —");
                GUI.color = Color.white;
                return;
            }

            List<Quotation> quotes = new List<Quotation>(request.quotes);
            SortQuotes(quotes);
            foreach (Quotation quote in quotes)
            {
                DrawQuoteRow(new Rect(rect.x + 16f, rowY, rect.width - 24f, QuoteRowHeight),
                    quote, request);
                rowY += QuoteRowHeight;
            }
        }

        private void DrawQuoteRow(Rect rect, Quotation quote, PurchaseRequest request)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            int outstanding = request.QuantityOutstanding;
            bool partial = quote.quantityOffered < outstanding;

            Rect Cell(int index)
            {
                float x = rect.x;
                for (int i = 0; i < index; i++)
                {
                    x += rect.width * QuoteColumnWidths[i];
                }

                return new Rect(x, rect.y + 2f,
                    rect.width * QuoteColumnWidths[index] - 4f, 22f);
            }

            Widgets.Label(Cell(0), quote.settlementName);

            // A partial quote is flagged, not hidden: §20 makes partial answers a first-class
            // outcome, and combining two suppliers is a legitimate move.
            Color previousColor = GUI.color;
            GUI.color = partial ? Color.yellow : previousColor;
            Widgets.Label(Cell(1), quote.quantityOffered.ToString());
            GUI.color = previousColor;

            Widgets.Label(Cell(2), $"{quote.unitPrice:F2}");
            Widgets.Label(Cell(3), quote.TotalPrice.ToString());
            Widgets.Label(Cell(4), $"{quote.leadTimeDays}d");
            Widgets.Label(Cell(5), quote.FulfillmentLabel);
            Widgets.Label(Cell(6), quote.distanceTiles < 0f ? "?" : $"{quote.distanceTiles:F0} t");
            if (request.IsOpen)
            {
                Rect actionCell = Cell(7);
                Rect buyRect = new Rect(
                    actionCell.x, rect.y + 1f, Mathf.Min(62f, actionCell.width), 24f);
                if (Widgets.ButtonText(buyRect, "Buy"))
                {
                    ConfirmPurchase(request, quote);
                }
            }

            if (ShouldBuildTooltip(rect))
            {
                TooltipHandler.TipRegion(rect,
                    $"{quote.settlementName} ({quote.factionName})\n" +
                    $"{quote.quantityOffered} offered; {outstanding} still wanted\n" +
                    (quote.offeredQuality.HasValue
                        ? $"Quality: {quote.offeredQuality.Value.GetLabel()}\n"
                        : "") +
                    (quote.offeredStuff != null ? $"Material: {quote.offeredStuff.label}\n" : "") +
                    $"{(quote.supplierDelivers ? "They deliver it" : "You collect it")}, " +
                    $"ready in {quote.leadTimeDays} days\n\n" +
                    quote.priceExplanation);
            }
        }

        private void DrawQuoteHeader(Rect rect)
        {
            float x = rect.x;
            for (int i = 0; i < QuoteColumnLabels.Length; i++)
            {
                float width = rect.width * QuoteColumnWidths[i];
                Rect cell = new Rect(x, rect.y, width - 4f, rect.height);
                if (i >= QuoteColumnLabels.Length - 1)
                {
                    x += width;
                    continue;
                }

                bool active = (int)quoteSortColumn == i;
                Widgets.DrawHighlightIfMouseover(cell);
                GUI.color = active ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(cell,
                    QuoteColumnLabels[i] + (active ? (quoteSortDescending ? " v" : " ^") : ""));
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(cell))
                {
                    if (active)
                    {
                        quoteSortDescending = !quoteSortDescending;
                    }
                    else
                    {
                        quoteSortColumn = (QuoteColumn)i;
                        quoteSortDescending = quoteSortColumn == QuoteColumn.Quantity ||
                                              quoteSortColumn == QuoteColumn.UnitPrice ||
                                              quoteSortColumn == QuoteColumn.Total;
                    }

                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }

                x += width;
            }
        }

        private void SortQuotes(List<Quotation> quotes)
        {
            Comparison<Quotation> comparison;
            switch (quoteSortColumn)
            {
                case QuoteColumn.Supplier:
                    comparison = (a, b) => string.Compare(
                        a.settlementName ?? "", b.settlementName ?? "",
                        StringComparison.CurrentCultureIgnoreCase);
                    break;
                case QuoteColumn.Quantity:
                    comparison = (a, b) => a.quantityOffered.CompareTo(b.quantityOffered);
                    break;
                case QuoteColumn.UnitPrice:
                    comparison = (a, b) => a.unitPrice.CompareTo(b.unitPrice);
                    break;
                case QuoteColumn.LeadTime:
                    comparison = (a, b) => a.leadTimeDays.CompareTo(b.leadTimeDays);
                    break;
                case QuoteColumn.Fulfillment:
                    comparison = (a, b) => a.supplierDelivers.CompareTo(b.supplierDelivers);
                    break;
                case QuoteColumn.Distance:
                    comparison = (a, b) => SortableQuoteDistance(a).CompareTo(SortableQuoteDistance(b));
                    break;
                default:
                    comparison = (a, b) => a.TotalPrice.CompareTo(b.TotalPrice);
                    break;
            }

            quotes.Sort((a, b) =>
            {
                int result = comparison(a, b);
                if (result != 0)
                {
                    return quoteSortDescending ? -result : result;
                }

                return a.id.CompareTo(b.id);
            });
        }

        private static float SortableQuoteDistance(Quotation quote)
        {
            return quote.distanceTiles < 0f ? float.MaxValue : quote.distanceTiles;
        }

        private static string RequestFulfillmentLabel(
            ProcurementFulfillmentPreference preference)
        {
            switch (preference)
            {
                case ProcurementFulfillmentPreference.SupplierDelivers:
                    return "supplier delivery only";
                case ProcurementFulfillmentPreference.PlayerPickup:
                    return "collection only";
                default:
                    return "delivery or collection";
            }
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

            int maximum = Mathf.Min(quote.quantityOffered, request.QuantityOutstanding);
            if (maximum <= 0)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_ConfirmQuantity(
                "Confirm purchase?",
                "Buy",
                maximum,
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

        private const float OrderHeaderHeight = 24f;
        private const float OrderRowHeight = 56f;
        private const float ClosedOrderSectionHeaderHeight = 32f;

        private static readonly float[] OrderColumnWidths =
            { 0.06f, 0.18f, 0.23f, 0.07f, 0.11f, 0.35f };

        private static readonly string[] OrderColumnLabels =
            { "#", "Buyer", "Goods", "Qty", "Value", "Status / ETA" };

        private void DrawOrderHeader(Rect rect)
        {
            float x = rect.x;
            for (int i = 0; i < OrderColumnLabels.Length; i++)
            {
                float width = rect.width * OrderColumnWidths[i];
                Rect cell = new Rect(x, rect.y, width - 4f, rect.height);
                bool active = (int)orderSortColumn == i;
                Widgets.DrawHighlightIfMouseover(cell);
                GUI.color = active ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                if (i == (int)OrderColumn.Quantity || i == (int)OrderColumn.Value)
                {
                    Text.Anchor = TextAnchor.UpperRight;
                }
                Widgets.Label(cell,
                    OrderColumnLabels[i] + (active ? (orderSortDescending ? " v" : " ^") : ""));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(cell))
                {
                    if (active)
                    {
                        orderSortDescending = !orderSortDescending;
                    }
                    else
                    {
                        orderSortColumn = (OrderColumn)i;
                        orderSortDescending = orderSortColumn == OrderColumn.Id ||
                                              orderSortColumn == OrderColumn.Quantity ||
                                              orderSortColumn == OrderColumn.Value;
                    }

                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }

                x += width;
            }
        }

        private void SortOrders(List<SalesOrder> orders)
        {
            Comparison<SalesOrder> comparison;
            switch (orderSortColumn)
            {
                case OrderColumn.Id:
                    comparison = (a, b) => a.id.CompareTo(b.id);
                    break;
                case OrderColumn.Buyer:
                    comparison = (a, b) => string.Compare(
                        a.settlementName ?? "", b.settlementName ?? "",
                        StringComparison.CurrentCultureIgnoreCase);
                    break;
                case OrderColumn.Goods:
                    comparison = (a, b) => string.Compare(
                        a.line?.ShortLabel() ?? "", b.line?.ShortLabel() ?? "",
                        StringComparison.CurrentCultureIgnoreCase);
                    break;
                case OrderColumn.Quantity:
                    comparison = (a, b) => a.Quantity.CompareTo(b.Quantity);
                    break;
                case OrderColumn.Value:
                    comparison = (a, b) =>
                        a.DiscountedTotalPayment.CompareTo(b.DiscountedTotalPayment);
                    break;
                default:
                    comparison = CompareOrderDeadline;
                    break;
            }

            orders.Sort((a, b) =>
            {
                int result = comparison(a, b);
                if (result != 0)
                {
                    return orderSortDescending ? -result : result;
                }

                return a.id.CompareTo(b.id);
            });
        }

        private static int CompareOrderDeadline(SalesOrder a, SalesOrder b)
        {
            bool aHasDeadline = a.IsOpen && !a.BuyerEnRoute;
            bool bHasDeadline = b.IsOpen && !b.BuyerEnRoute;
            if (aHasDeadline != bHasDeadline)
            {
                return aHasDeadline ? -1 : 1;
            }

            if (aHasDeadline)
            {
                return a.deadlineTick.CompareTo(b.deadlineTick);
            }

            if (a.BuyerEnRoute != b.BuyerEnRoute)
            {
                return a.BuyerEnRoute ? -1 : 1;
            }

            if (a.BuyerEnRoute && a.buyerArrivalTick >= 0 && b.buyerArrivalTick >= 0)
            {
                return a.buyerArrivalTick.CompareTo(b.buyerArrivalTick);
            }

            return a.status.CompareTo(b.status);
        }

        private void DrawOrderRow(Rect rect, SalesOrder order, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);

            Rect Cell(int column)
            {
                float x = rect.x + 4f;
                for (int i = 0; i < column; i++)
                {
                    x += rect.width * OrderColumnWidths[i];
                }

                return new Rect(x, rect.y + 4f,
                    rect.width * OrderColumnWidths[column] - 8f, 22f);
            }

            Widgets.Label(Cell(0), order.id.ToString());
            Widgets.LabelFit(Cell(1), order.settlementName);
            Widgets.LabelFit(Cell(2), order.line?.ShortLabel() ?? "<missing>");
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(Cell(3), order.Quantity.ToString());
            Widgets.LabelFit(Cell(4), $"{order.DiscountedTotalPayment:N0} silver");
            Text.Anchor = TextAnchor.UpperLeft;

            // §17: show progress and time remaining, and warn rather than fail silently.
            Color colour = Color.white;
            if (order.BuyerEnRoute)
            {
                colour = new Color(0.6f, 0.85f, 1f);
            }
            else if (order.IsOpen)
            {
                if (order.DaysRemaining < 1f)
                {
                    colour = Color.yellow;
                }
            }
            else
            {
                colour = order.status == SalesOrderStatus.Completed
                    ? new Color(0.6f, 0.9f, 0.6f)
                    : new Color(0.9f, 0.6f, 0.6f);
            }

            GUI.color = colour;
            Widgets.LabelFit(Cell(5), OrderStatusEtaText(order));
            GUI.color = Color.white;

            if (!order.IsOpen && !order.outcomeNote.NullOrEmpty() && ShouldBuildTooltip(rect))
            {
                TooltipHandler.TipRegion(rect, "Outcome: " + order.outcomeNote);
            }

            if (!order.IsOpen)
            {
                return;
            }

            // Buyer pickup: the player declares the goods ready and the buyer travels (§25.2).
            if (order.CanMarkReady)
            {
                // A recorded colony remains authoritative and may not be redirected if it is
                // gone. Legacy cycle orders with no record can adopt the colony the player is
                // acting from; the service persists that choice after validation succeeds.
                Map map;
                if (order.fulfillmentMap != null)
                {
                    map = Find.Maps?.Contains(order.fulfillmentMap) == true
                        ? order.fulfillmentMap
                        : null;
                }
                else
                {
                    Map currentMap = Find.CurrentMap;
                    map = currentMap?.IsPlayerHome == true
                        ? currentMap
                        : Find.AnyPlayerHomeMap;
                }
                OrderValidationResult validation = OrderValidator.ValidateColony(order, map);
                bool enough = validation.Success;

                Rect readyRect = new Rect(rect.xMax - 210f, rect.y + 27f, 110f, 26f);
                // RimWorld draws an inactive text button like a live one. Keep this clickable
                // so an invalid attempt reaches the service and explains the refusal.
                if (Widgets.ButtonText(readyRect, "Mark ready"))
                {
                    SalesOrder readyOrder = order;
                    Map readyMap = map;
                    if (!enough)
                    {
                        SalesOrderService.MarkReadyForPickup(readyOrder, readyMap);
                    }
                    else
                    {
                        // Marking an animal order ready commits these particular animals, so the
                        // bond warning belongs here rather than only at a caravan handover.
                        string bondWarning =
                            SalesOrderService.BuildBondedAnimalWarning(readyOrder, readyMap);
                        if (bondWarning.NullOrEmpty())
                        {
                            SalesOrderService.MarkReadyForPickup(readyOrder, readyMap);
                        }
                        else
                        {
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                                bondWarning,
                                () => SalesOrderService.MarkReadyForPickup(readyOrder, readyMap),
                                destructive: true));
                        }
                    }
                }

                if (ShouldBuildTooltip(readyRect))
                {
                    int pickupDays = SalesOrderService.EstimateBuyerPickupTravelDays(
                        order.buyerPickupDistanceTiles);
                    TooltipHandler.TipRegion(readyRect, enough
                        ? $"Tell {order.settlementName} the goods are ready. Their caravan will " +
                          $"take approximately {pickupDays} days to arrive and collect them " +
                          "from your storage."
                        : validation.Summary());
                }
            }

            Rect cancelRect = new Rect(rect.xMax - 90f, rect.y + 27f, 80f, 26f);
            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Cancel order #{order.id} for {order.settlementName}? " +
                    "You will not be paid for anything already delivered.",
                    () => SalesOrderService.Cancel(order),
                    destructive: true));
            }
        }

        internal static string OrderStatusEtaText(SalesOrder order)
        {
            if (order.BuyerEnRoute)
            {
                return order.buyerArrivalTick >= 0
                    ? $"En route — {order.DaysUntilBuyerArrives:F1}d"
                    : "Collection dispatched";
            }

            if (order.IsOpen)
            {
                string progress = order.deliveredQuantity > 0
                    ? $" — {order.deliveredQuantity}/{order.Quantity} delivered"
                    : "";
                return $"{order.DaysRemaining:F1}d left{progress}";
            }

            return order.status.ToString();
        }

    }
}
