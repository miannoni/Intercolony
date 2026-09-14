namespace Intercolony
{
    /// <summary>
    /// Stable, derived rapid-logistics option for a settlement. This is intentionally not
    /// persisted: the owning economic profile is regenerated from the world and settlement seed.
    /// </summary>
    public enum SettlementRapidLogisticsCapability
    {
        ConventionalTransportOnly,
        DropPodsAvailable
    }

    public static class SettlementRapidLogisticsCapabilityUtility
    {
        /// <summary>Short player-facing label for the two capability states.</summary>
        public static string Label(this SettlementRapidLogisticsCapability capability)
        {
            return capability == SettlementRapidLogisticsCapability.DropPodsAvailable
                ? "Drop pods available"
                : "Conventional transport only";
        }
    }
}
