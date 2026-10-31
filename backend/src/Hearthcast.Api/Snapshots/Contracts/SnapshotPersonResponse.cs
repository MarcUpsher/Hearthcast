using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Classification;

namespace Hearthcast.Api.Snapshots.Contracts;

public sealed record SnapshotPersonResponse(
    Guid Id,
    Guid HouseholdId,
    Guid SnapshotId,
    Guid PersonId,
    string DisplayName,
    bool ParticipatesInHouseholdFinances)
{
    internal static SnapshotPersonResponse From(SnapshotPerson value) => new(
        value.Id,
        value.HouseholdId,
        value.SnapshotId,
        value.PersonId,
        value.DisplayName,
        value.ParticipatesInHouseholdFinances);
}
