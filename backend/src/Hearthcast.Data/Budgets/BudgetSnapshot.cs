using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Budgets;

public sealed class BudgetSnapshot
{
    private readonly List<BudgetEntry> _entries = [];
    private BudgetSnapshot() { }

    internal BudgetSnapshot(Guid id, Household household, DateOnly effectiveFrom,
        IReadOnlyList<BudgetEntryInput> entries, string creationRequestHash,
        BudgetSnapshot? copiedFrom = null, string? notes = null)
    {
        BudgetRules.Identity(id, creationRequestHash);
        Id = id;
        HouseholdId = household.Id;
        EffectiveFrom = BudgetRules.Date(effectiveFrom);
        if (copiedFrom is not null) RequireEarlierSource(copiedFrom, EffectiveFrom);
        CopiedFromBudgetSnapshotId = copiedFrom?.Id;
        CreationRequestHash = creationRequestHash;
        Notes = SpendingSetupRules.Notes(notes);
        ArgumentNullException.ThrowIfNull(entries);
        var saved = entries.Select(input => new BudgetEntry(this, input)).ToArray();
        if (saved.Select(e => e.BudgetItemId).Distinct().Count() != saved.Length)
            throw new InvalidValueException("Include a budget item only once per plan.");
        foreach (var entry in saved) { RequireUnique(entry); _entries.Add(entry); }
        CreatedAtUtc = SpendingValueRules.Now();
        UpdatedAtUtc = CreatedAtUtc;
        Revision = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid Revision { get; private set; }
    public string CreationRequestHash { get; private set; } = string.Empty;
    public Guid? CopiedFromBudgetSnapshotId { get; private set; }
    public IReadOnlyList<BudgetEntry> Entries => _entries.AsReadOnly();
    public bool IsComplete => _entries.All(e => e.PlannedAmount.HasValue);

    internal void CorrectAmount(Guid expectedRevision, Guid entryId, decimal? amount, DateOnly? confirmedOn, string? notes)
    {
        RequireRevision(expectedRevision);
        FindEntry(entryId).CorrectAmount(amount, confirmedOn, notes);
        Touch();
    }

    // Explicit metadata/value correction; build first so failed validation cannot partly mutate an entry.
    // The service detaches incompatible incoming links before saving this correction.
    internal void CorrectEntry(Guid expectedRevision, Guid entryId, BudgetEntryInput input)
    {
        RequireRevision(expectedRevision);
        var entry = FindEntry(entryId);
        var candidate = new BudgetEntry(this, input, entry);
        RequireUnique(candidate, entryId);
        entry.ApplyCorrection(candidate);
        Touch();
    }

    // Preserve the saved amount/provenance, without confirming it or rereading its carry source.
    internal void CorrectEntryMetadata(Guid expectedRevision, Guid entryId, BudgetEntryMetadata metadata)
    {
        RequireRevision(expectedRevision);
        FindEntry(entryId).CorrectMetadata(metadata, candidate => RequireUnique(candidate, entryId));
        Touch();
    }

    internal void AddEntry(Guid expectedRevision, BudgetEntryInput input)
    {
        RequireRevision(expectedRevision);
        var entry = new BudgetEntry(this, input);
        RequireUnique(entry);
        _entries.Add(entry);
        Touch();
    }

    internal BudgetEntry RemoveEntry(Guid expectedRevision, Guid entryId)
    {
        RequireRevision(expectedRevision);
        var entry = FindEntry(entryId);
        _entries.Remove(entry);
        Touch();
        return entry;
    }

    // Before changing dates the service also validates/detaches incoming links in the same transaction.
    // Retained outgoing links are checked here against resolved source aggregates.
    internal void CorrectHeader(Guid expectedRevision, DateOnly effectiveFrom, string? notes,
        IReadOnlyCollection<BudgetSnapshot>? retainedSources = null)
    {
        RequireRevision(expectedRevision);
        var date = BudgetRules.Date(effectiveFrom);
        var savedNotes = SpendingSetupRules.Notes(notes);
        if (date != EffectiveFrom)
        {
            var sources = retainedSources ?? [];
            if (CopiedFromBudgetSnapshotId.HasValue)
                RequireEarlierSource(sources.SingleOrDefault(s => s.Id == CopiedFromBudgetSnapshotId)
                    ?? throw new InvalidValueException("Resolve or detach the template source before changing the date."), date);
            foreach (var entry in _entries.Where(e => e.CarriedFromBudgetEntryId.HasValue))
            {
                var source = sources.SingleOrDefault(s => s.Entries.Any(e => e.Id == entry.CarriedFromBudgetEntryId))
                    ?? throw new InvalidValueException("Resolve or detach carry sources before changing the date.");
                entry.RequireCompatibleSource(Id, date, source, source.Entries.Single(e => e.Id == entry.CarriedFromBudgetEntryId));
            }
        }
        EffectiveFrom = date;
        Notes = savedNotes;
        Touch();
    }

    // Clear only informational references. Copied values and their provenance remain independent.
    internal void DetachSources(Guid expectedRevision, IReadOnlyCollection<Guid> receivingEntryIds, bool clearTemplate = false)
    {
        RequireRevision(expectedRevision);
        ArgumentNullException.ThrowIfNull(receivingEntryIds);
        var entries = receivingEntryIds.Distinct().Select(FindEntry).ToArray();
        var changed = (clearTemplate && CopiedFromBudgetSnapshotId.HasValue) || entries.Any(e => e.CarriedFromBudgetEntryId.HasValue);
        foreach (var entry in entries) entry.ClearSource();
        if (clearTemplate) CopiedFromBudgetSnapshotId = null;
        if (changed) Touch();
    }

    private BudgetEntry FindEntry(Guid id) => _entries.SingleOrDefault(e => e.Id == id)
        ?? throw new InvalidValueException("The entry does not belong to this budget plan.");

    private void RequireUnique(BudgetEntry candidate, Guid? except = null)
    {
        if (_entries.Any(e => e.Id != except && e.BudgetItemId == candidate.BudgetItemId))
            throw new InvalidValueException("Include a budget item only once per plan.");
        if (candidate.Kind == BudgetItemKind.Allowance && _entries.Any(e => e.Id != except && e.Kind == BudgetItemKind.Allowance
            && e.ExpenseCategoryId == candidate.ExpenseCategoryId && e.CurrencyCode == candidate.CurrencyCode))
            throw new InvalidValueException("Include only one allowance per category and currency in a plan.");
    }

    private void RequireEarlierSource(BudgetSnapshot source, DateOnly effectiveFrom)
    {
        if (source.Id == Id || source.HouseholdId != HouseholdId || source.EffectiveFrom >= effectiveFrom)
            throw new InvalidValueException("Select a strictly earlier template plan in this household.");
    }

    private void RequireRevision(Guid expectedRevision)
    {
        if (Revision != expectedRevision) throw new BudgetSnapshotConflictException();
    }

    private void Touch()
    {
        Revision = Guid.NewGuid();
        UpdatedAtUtc = SpendingValueRules.Now();
    }
}
