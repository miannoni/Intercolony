namespace Intercolony
{
    /// <summary>The equipment level a job posting requests from the source settlement.</summary>
    public enum LaborEquipmentLevel
    {
        /// <summary>No gear requirement; this is today's behavior and the default for old postings.</summary>
        Any,

        /// <summary>The source settlement supplies no bondable weapon or apparel.</summary>
        None,

        /// <summary>The source settlement supplies a standard bondable loadout.</summary>
        Standard,

        /// <summary>The source settlement supplies a more capable professional bondable loadout.</summary>
        Professional,

        /// <summary>The source settlement supplies an elite bondable loadout.</summary>
        Elite
    }
}
