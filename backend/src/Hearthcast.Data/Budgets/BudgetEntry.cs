using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Budgets;

public sealed class BudgetEntry
{
    private BudgetEntry() { }

    internal BudgetEntry(BudgetSnapshot parent, BudgetEntryInput input, BudgetEntry? existing = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Metadata);
        Id = Guid.NewGuid();
        HouseholdId = parent.HouseholdId;
        BudgetSnapshotId = parent.Id;
        if (input.CarrySourceSnapshot is null && input.CarrySourceEntryId is null)
        {
            SetMetadata(input.Metadata, existing is not null && existing.BudgetItemId == input.Metadata.BudgetItemId
                ? existing.CurrencyCode : null);
            SetAmount(input.PlannedAmount, input.ConfirmedOn);
            return;
        }
        if (input.CarrySourceSnapshot is null || input.CarrySourceEntryId is null
            || input.PlannedAmount is not null || input.ConfirmedOn is not null)
            throw new InvalidValueException("Carry a resolved source entry or explicitly enter an amount, not both.");
        var source = input.CarrySourceSnapshot.Entries.SingleOrDefault(e => e.Id == input.CarrySourceEntryId)
            ?? throw new InvalidValueException("The carry source entry must belong to its source plan.");
        SetMetadata(input.Metadata, source.CurrencyCode);
        RequireCompatibleSource(parent.Id, parent.EffectiveFrom, input.CarrySourceSnapshot, source);
        PlannedAmount = source.PlannedAmount;
        LastConfirmedDate = source.LastConfirmedDate;
        ValueOrigin = source.PlannedAmount is null ? BudgetValueOrigin.Unrecorded : BudgetValueOrigin.CarriedForward;
        CarriedFromBudgetEntryId = source.PlannedAmount is null ? null : source.Id;
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid BudgetSnapshotId { get; private set; }
    public Guid BudgetItemId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public string ItemName { get; private set; } = string.Empty;
    public Guid ExpenseCategoryId { get; private set; }
    public string ExpenseCategoryName { get; private set; } = string.Empty;
    public Guid? MerchantId { get; private set; }
    public string? MerchantName { get; private set; }
    public decimal? PlannedAmount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public BudgetFrequency Frequency { get; private set; }
    public int IntervalCount { get; private set; }
    public string? Notes { get; private set; }
    public BudgetValueOrigin ValueOrigin { get; private set; }
    public DateOnly? LastConfirmedDate { get; private set; }
    public Guid? CarriedFromBudgetEntryId { get; private set; }

    internal void CorrectAmount(decimal? amount, DateOnly? confirmedOn, string? notes)
    {
        var savedNotes = SpendingSetupRules.Notes(notes);
        SetAmount(amount, confirmedOn);
        Notes = savedNotes;
    }

    // No source lookup: even a detached carry keeps its independent saved value/provenance.
    internal void CorrectMetadata(BudgetEntryMetadata metadata)
    {
        var candidate = new BudgetEntry { HouseholdId = HouseholdId };
        candidate.SetMetadata(metadata, CurrencyCode);
        if (candidate.BudgetItemId != BudgetItemId || candidate.CurrencyCode != CurrencyCode
            || candidate.Frequency != Frequency || candidate.IntervalCount != IntervalCount)
            throw new InvalidValueException("Changing the item, currency or recurrence requires an explicit value correction.");
        ApplyMetadata(candidate);
    }

    internal void ApplyCorrection(BudgetEntry candidate)
    {
        ApplyMetadata(candidate);
        PlannedAmount = candidate.PlannedAmount;
        ValueOrigin = candidate.ValueOrigin;
        LastConfirmedDate = candidate.LastConfirmedDate;
        CarriedFromBudgetEntryId = candidate.CarriedFromBudgetEntryId;
    }

    private void ApplyMetadata(BudgetEntry candidate)
    {
        BudgetItemId = candidate.BudgetItemId;
        ItemCode = candidate.ItemCode;
        ItemName = candidate.ItemName;
        ExpenseCategoryId = candidate.ExpenseCategoryId;
        ExpenseCategoryName = candidate.ExpenseCategoryName;
        MerchantId = candidate.MerchantId;
        MerchantName = candidate.MerchantName;
        CurrencyCode = candidate.CurrencyCode;
        Frequency = candidate.Frequency;
        IntervalCount = candidate.IntervalCount;
        Notes = candidate.Notes;
    }

    internal void ClearSource() => CarriedFromBudgetEntryId = null;

    internal void RequireCompatibleSource(Guid receivingSnapshotId, DateOnly effectiveFrom, BudgetSnapshot source, BudgetEntry entry)
    {
        if (source.Id == receivingSnapshotId || source.HouseholdId != HouseholdId || source.EffectiveFrom >= effectiveFrom
            || entry.BudgetSnapshotId != source.Id || entry.HouseholdId != HouseholdId || entry.BudgetItemId != BudgetItemId
            || entry.CurrencyCode != CurrencyCode || entry.Frequency != Frequency || entry.IntervalCount != IntervalCount)
            throw new InvalidValueException("Carry from a strictly earlier plan with the same household, item, currency and recurrence.");
    }

    private void SetAmount(decimal? amount, DateOnly? confirmedOn)
    {
        var value = BudgetRules.Amount(amount);
        if (value.HasValue != confirmedOn.HasValue)
            throw new InvalidValueException("An entered amount requires its confirmation date; an unrecorded amount has no confirmation date.");
        if (confirmedOn.HasValue) BudgetRules.Date(confirmedOn.Value);
        PlannedAmount = value;
        LastConfirmedDate = confirmedOn;
        ValueOrigin = value.HasValue ? BudgetValueOrigin.Confirmed : BudgetValueOrigin.Unrecorded;
        CarriedFromBudgetEntryId = null;
    }

    private void SetMetadata(BudgetEntryMetadata metadata, string? savedCurrency = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.HouseholdId != HouseholdId || metadata.BudgetItemId == Guid.Empty || metadata.ExpenseCategoryId == Guid.Empty
            || metadata.MerchantId == Guid.Empty || metadata.MerchantId.HasValue != (metadata.MerchantName is not null))
            throw new InvalidValueException("Supply household-scoped item/category identities and paired merchant identity/name.");
        BudgetItemId = metadata.BudgetItemId;
        ItemCode = SpendingSetupRules.Code(metadata.ItemCode);
        ItemName = SpendingSetupRules.Text(metadata.ItemName);
        ExpenseCategoryId = metadata.ExpenseCategoryId;
        ExpenseCategoryName = SpendingSetupRules.Text(metadata.ExpenseCategoryName);
        MerchantId = metadata.MerchantId;
        MerchantName = metadata.MerchantName is null ? null : SpendingSetupRules.Text(metadata.MerchantName);
        CurrencyCode = BudgetRules.Currency(metadata.CurrencyCode, savedCurrency);
        BudgetRules.Recurrence(metadata.Frequency, metadata.IntervalCount);
        Frequency = metadata.Frequency;
        IntervalCount = metadata.IntervalCount;
        Notes = SpendingSetupRules.Notes(metadata.Notes);
    }
}
