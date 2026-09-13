using Verse;

namespace Intercolony
{
    /// <summary>
    /// One already-generated RFQ quotation that has not reached the request yet.
    ///
    /// The quotation itself is persisted here rather than regenerated at arrival time: pricing
    /// randomness is consumed when the request is made, while this record only changes when the
    /// player learns the result.
    /// </summary>
    public class PendingRfqResponse : IExposable
    {
        public int requestId;
        public Quotation quote;
        public int arrivalTick;

        /// <summary>Whether this queue entry has the minimum shape needed after loading.</summary>
        public bool IsValidAfterLoad => requestId > 0 && arrivalTick >= 0 &&
                                         quote != null && quote.quantityOffered > 0;

        public void ExposeData()
        {
            Scribe_Values.Look(ref requestId, "requestId", 0);
            Scribe_Deep.Look(ref quote, "quote");
            Scribe_Values.Look(ref arrivalTick, "arrivalTick", 0);
        }
    }
}
