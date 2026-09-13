using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Receiving-location marker state is saved in the map's save block and is independent of the
    /// world-level IntercolonyWorldComponent schema and migration ladder.
    /// </summary>
    public class ReceivingLocationMapComponent : MapComponent
    {
        private List<int> receivingZoneIds = new List<int>();
        private List<Building_Storage> receivingBuildings = new List<Building_Storage>();

        public ReceivingLocationMapComponent(Map map) : base(map)
        {
        }

        public static ReceivingLocationMapComponent For(Map map)
        {
            return map?.GetComponent<ReceivingLocationMapComponent>();
        }

        public bool IsReceiving(Zone_Stockpile zone)
        {
            if (!IsOnThisMap(zone))
            {
                return false;
            }

            return receivingZoneIds != null && receivingZoneIds.Contains(zone.ID);
        }

        public bool IsReceiving(Building_Storage building)
        {
            return IsValidReceivingBuilding(building) &&
                receivingBuildings != null && receivingBuildings.Contains(building);
        }

        public void SetReceiving(Zone_Stockpile zone, bool receiving)
        {
            if (!IsOnThisMap(zone))
            {
                return;
            }

            EnsureCollections();
            if (receiving)
            {
                if (!receivingZoneIds.Contains(zone.ID))
                {
                    receivingZoneIds.Add(zone.ID);
                }
            }
            else
            {
                receivingZoneIds.RemoveAll(id => id == zone.ID);
            }
        }

        public void SetReceiving(Building_Storage building, bool receiving)
        {
            if (building == null)
            {
                return;
            }

            EnsureCollections();
            if (receiving)
            {
                if (!IsValidReceivingBuilding(building))
                {
                    return;
                }

                if (!receivingBuildings.Contains(building))
                {
                    receivingBuildings.Add(building);
                }
            }
            else
            {
                receivingBuildings.RemoveAll(markedBuilding => markedBuilding == building);
            }
        }

        public bool AnyConfigured
        {
            get
            {
                return (receivingZoneIds != null && receivingZoneIds.Count > 0) ||
                    (receivingBuildings != null && receivingBuildings.Count > 0);
            }
        }

        /// <summary>
        /// Marked stockpiles and shelves share this vanilla interface. A delivery path can check
        /// HaulDestinationEnabled and Accepts, then test each live slot cell with
        /// StoreUtility.IsValidStorageFor so the destination's own filter and current footprint
        /// remain authoritative.
        /// </summary>
        public IEnumerable<ISlotGroupParent> ReceivingDestinations
        {
            get
            {
                if (receivingZoneIds != null)
                {
                    for (int i = 0; i < receivingZoneIds.Count; i++)
                    {
                        Zone_Stockpile zone = FindStockpileById(receivingZoneIds[i]);
                        if (zone != null)
                        {
                            yield return zone;
                        }
                    }
                }

                if (receivingBuildings != null)
                {
                    for (int i = 0; i < receivingBuildings.Count; i++)
                    {
                        Building_Storage building = receivingBuildings[i];
                        if (IsValidReceivingBuilding(building))
                        {
                            yield return building;
                        }
                    }
                }
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureCollections();

            // Zones are owned and restored by ZoneManager, so a missing ID means the zone was
            // deleted or otherwise did not survive loading this map.
            receivingZoneIds.RemoveAll(id => FindStockpileById(id) == null);

            // Scribe_References can resolve a missing or stale reference as null; a despawned or
            // destroyed building is not a usable receiving destination either.
            receivingBuildings.RemoveAll(building => !IsValidReceivingBuilding(building));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref receivingZoneIds, "receivingZoneIds", LookMode.Value);
            Scribe_Collections.Look(ref receivingBuildings, "receivingBuildings", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureCollections();
            }
        }

        private void EnsureCollections()
        {
            if (receivingZoneIds == null)
            {
                receivingZoneIds = new List<int>();
            }

            if (receivingBuildings == null)
            {
                receivingBuildings = new List<Building_Storage>();
            }
        }

        private bool IsOnThisMap(Zone_Stockpile zone)
        {
            return map != null && zone != null && zone.zoneManager != null && zone.Map == map;
        }

        private bool IsValidReceivingBuilding(Building_Storage building)
        {
            return map != null && building != null && building.Spawned && !building.Destroyed &&
                building.MapHeld == map;
        }

        private Zone_Stockpile FindStockpileById(int id)
        {
            if (map?.zoneManager?.AllZones == null)
            {
                return null;
            }

            List<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Stockpile stockpile && stockpile.ID == id)
                {
                    return stockpile;
                }
            }

            return null;
        }
    }
}
