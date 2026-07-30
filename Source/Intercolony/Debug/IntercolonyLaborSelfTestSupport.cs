using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Silver plumbing shared by the labor and payroll self-tests.
    ///
    /// Both need to control exactly how much silver the colony has: the payroll test has to
    /// *starve* the colony, because §38's requirement is that a shortfall creates arrears rather
    /// than blocking, and that branch cannot be proven with money in the bank.
    ///
    /// Every removal and addition is netted in <see cref="netTaken"/> so
    /// <see cref="RestoreLedger"/> can put the colony back roughly where it started. A dev test
    /// that quietly eats a colony's treasury is a worse bug than the one it was written to catch.
    /// </summary>
    public static class IntercolonyLaborSelfTestSupport
    {
        private static int netTaken;

        /// <summary>Silver the test has removed and not yet given back. Positive means the colony is down.</summary>
        public static int NetTaken => netTaken;

        public static void ResetLedger()
        {
            netTaken = 0;
        }

        /// <summary>Tops colony storage up to at least <paramref name="needed"/> silver.</summary>
        public static int EnsureSilver(Map map, int needed)
        {
            int available = PurchaseOrderService.CountColonySilver(map);
            if (available >= needed)
            {
                return 0;
            }

            return AddSilver(map, needed - available);
        }

        /// <summary>Puts silver into storage cells, creating a temporary stockpile only if there is none.</summary>
        public static int AddSilver(Map map, int amount)
        {
            if (map == null || amount <= 0)
            {
                return 0;
            }

            int stacksNeeded = Mathf.CeilToInt(amount / (float)ThingDefOf.Silver.stackLimit);
            List<IntVec3> cells = FindStorageCells(map, stacksNeeded, out Zone_Stockpile created);

            int remaining = amount;
            int placed = 0;
            foreach (IntVec3 cell in cells)
            {
                if (remaining <= 0)
                {
                    break;
                }

                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = Mathf.Min(remaining, ThingDefOf.Silver.stackLimit);
                remaining -= silver.stackCount;
                placed += silver.stackCount;
                GenSpawn.Spawn(silver, cell, map);
            }

            if (created != null)
            {
                // Left in place on purpose: deleting the zone would make the silver we just
                // placed stop counting as stored, which is the opposite of the intent.
                IntercolonyLog.Verbose("Self-test created a temporary stockpile to hold test silver.");
            }

            netTaken -= placed;
            return placed;
        }

        /// <summary>
        /// Removes all stored silver, so the arrears branch can be exercised. Recorded, and given
        /// back by <see cref="RestoreLedger"/>.
        /// </summary>
        public static int StripSilver(Map map)
        {
            if (map == null)
            {
                return 0;
            }

            int taken = PurchaseOrderService.CountColonySilver(map);
            if (taken > 0)
            {
                PurchaseOrderService.TryTakeSilver(map, taken);
                netTaken += taken;
            }

            return taken;
        }

        /// <summary>Returns whatever the test consumed net, so the colony is not left poorer.</summary>
        public static int RestoreLedger(Map map)
        {
            if (netTaken <= 0)
            {
                netTaken = 0;
                return 0;
            }

            int owed = netTaken;
            netTaken = 0;

            // AddSilver credits the ledger, so reset again afterwards rather than letting it go
            // negative and confusing a later run.
            int given = AddSilver(map, owed);
            netTaken = 0;
            return given;
        }

        private static List<IntVec3> FindStorageCells(Map map, int wanted, out Zone_Stockpile created)
        {
            created = null;
            List<IntVec3> cells = new List<IntVec3>();

            // Each stack needs its own empty cell *inside* a storage group: a stack one tile
            // outside a stockpile is not IsInAnyStorage and would not count as colony silver.
            foreach (SlotGroup group in map.haulDestinationManager.AllGroupsListInPriorityOrder)
            {
                foreach (IntVec3 candidate in group.CellsList)
                {
                    if (candidate.Standable(map) && candidate.GetFirstItem(map) == null)
                    {
                        cells.Add(candidate);
                        if (cells.Count >= wanted)
                        {
                            return cells;
                        }
                    }
                }
            }

            if (cells.Count >= wanted)
            {
                return cells;
            }

            created = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(created);

            IntVec3 root = DropCellFinder.TradeDropSpot(map);
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(root, 8f, useCenter: true))
            {
                if (cells.Count >= wanted)
                {
                    break;
                }

                if (candidate.InBounds(map) && candidate.Standable(map) &&
                    candidate.GetFirstItem(map) == null &&
                    map.zoneManager.ZoneAt(candidate) == null)
                {
                    created.AddCell(candidate);
                    cells.Add(candidate);
                }
            }

            return cells;
        }
    }
}
