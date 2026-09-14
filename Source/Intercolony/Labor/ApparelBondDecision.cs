namespace Intercolony
{
    /// <summary>Whether vanilla apparel management may remove an employee's issued gear.</summary>
    public enum ApparelBondDecision
    {
        /// <summary>The player has not yet been asked; this is the default for new and migrated employees.</summary>
        Pending,

        /// <summary>Original bonded apparel may be removed and each removed item is bought out of the bond.</summary>
        Allowed,

        /// <summary>Original bonded apparel may not be removed, and the prompt does not repeat.</summary>
        Denied
    }
}
