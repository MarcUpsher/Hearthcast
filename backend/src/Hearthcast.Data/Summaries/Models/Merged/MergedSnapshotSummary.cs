using Hearthcast.Data.Classification;

namespace Hearthcast.Data.Summaries.Models.Merged;

public sealed record MergedAmount(string CurrencyCode, decimal? KnownSubtotal, int MissingValueCount,
    int MissingRateCount, int CarriedEntryCount)
{
    public bool IsComplete => MissingValueCount == 0 && MissingRateCount == 0;
}
public sealed record MergedEntry(string Key, string Name, string Description, string SourceCurrency,
    decimal? SourceAmount, string Status, string? InstitutionName, string? FundName, IReadOnlyList<MergedAmount> Amounts, MergedEntryKind Kind);
public sealed record MergedGroup(Guid GroupId, string GroupName, IReadOnlyList<MergedAmount> Amounts,
    IReadOnlyList<MergedEntry> Entries);
public sealed record MergedCategory(Guid CategoryId, string CategoryName, FinancialType FinancialType,
    IReadOnlyList<MergedAmount> Amounts, IReadOnlyList<MergedGroup> Groups);
public sealed record MergedView(IReadOnlyList<MergedAmount> Amounts, IReadOnlyList<MergedCategory> Categories);
public sealed record MergedPerson(Guid PersonId, MergedView View);
public sealed record MergedSnapshotSummary(Guid SnapshotId, Guid Revision, IReadOnlyList<string> Currencies,
    MergedView Household, IReadOnlyList<MergedPerson> HouseholdPeople,
    IReadOnlyList<MergedPerson> IndividualPeople, MergedView AllRecorded);
