namespace Hearthcast.Data.Summaries.Models;

public sealed record SnapshotPersonSummary(
    Guid SnapshotPersonId, Guid PersonId, string DisplayName, bool ParticipatesInHouseholdFinances);
