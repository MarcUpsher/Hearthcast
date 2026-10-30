namespace Hearthcast.Data.Summaries.Models;

/// <summary>Counts contributing entries, not balance fields. A carried count includes only known contributions.</summary>
public sealed record SnapshotAmountSubtotal(decimal KnownSubtotal, int MissingEntryCount, int CarriedForwardEntryCount)
{
    public bool IsComplete => MissingEntryCount == 0;
}
