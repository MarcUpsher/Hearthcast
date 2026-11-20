using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

public sealed class SpendingLine
{
    private SpendingLine() { }

    internal SpendingLine(SpendingRecord record, int position, SpendingLineInput input)
    {
        if (input.Category.HouseholdId != record.HouseholdId ||
            (input.Item is not null && input.Item.HouseholdId != record.HouseholdId))
            throw new InvalidValueException("Line references must belong to the spending record's household.");
        Id = Guid.NewGuid();
        HouseholdId = record.HouseholdId;
        SpendingRecordId = record.Id;
        CurrencyCode = record.CurrencyCode;
        CurrencyMinorUnits = record.CurrencyMinorUnits;
        Position = position;
        ExpenseCategoryId = input.Category.Id;
        ExpenseCategoryName = input.Category.Name;
        SpendingItemId = input.Item?.Id;
        ItemCode = input.Item?.Code;
        ItemName = input.Item?.Name;
        UnitKey = input.Item?.UnitKey ?? (input.UnitKey is null ? null : SpendingSetupRules.UnitKey(input.UnitKey));
        UnitLabel = input.Item?.UnitLabel ?? (input.UnitLabel is null ? null : SpendingSetupRules.Text(input.UnitLabel));
        if (input.Item is not null && (input.UnitKey is not null || input.UnitLabel is not null))
            throw new InvalidValueException("Linked lines use the catalogue item's unit; do not supply another unit.");
        if ((UnitKey is null) != (UnitLabel is null) || (input.Quantity.HasValue && UnitKey is null))
            throw new InvalidValueException("A quantity needs a unit key and label; unit metadata must be supplied together.");
        Description = SpendingSetupRules.Notes(input.Description);
        IsAdditionalCost = input.IsAdditionalCost;
        if (IsAdditionalCost && (SpendingItemId.HasValue || UnitKey is not null || Description is null))
            throw new InvalidValueException("Additional costs need a description and cannot have catalogue items or units.");
        ReceiptDescription = SpendingSetupRules.Notes(input.ReceiptDescription);
        MerchantItemReference = SpendingSetupRules.Notes(input.MerchantItemReference);
        CorrectValues(input.NetAmount, input.Quantity, input.UnitPrice, input.PromotionStatus, input.Notes);
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid SpendingRecordId { get; private set; }
    public int Position { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public int CurrencyMinorUnits { get; private set; }
    public Guid? SpendingItemId { get; private set; }
    public string? ItemCode { get; private set; }
    public string? ItemName { get; private set; }
    public string? Description { get; private set; }
    public Guid ExpenseCategoryId { get; private set; }
    public string ExpenseCategoryName { get; private set; } = string.Empty;
    public string? UnitKey { get; private set; }
    public string? UnitLabel { get; private set; }
    public string? ReceiptDescription { get; private set; }
    public string? MerchantItemReference { get; private set; }
    public decimal? Quantity { get; private set; }
    public decimal? UnitPrice { get; private set; }
    public decimal NetAmount { get; private set; }
    public bool IsAdditionalCost { get; private set; }
    public PromotionStatus PromotionStatus { get; private set; }
    public string? Notes { get; private set; }

    internal void CorrectValues(decimal netAmount, decimal? quantity, decimal? unitPrice,
        PromotionStatus promotionStatus, string? notes)
    {
        var amount = SpendingValueRules.Amount(netAmount, CurrencyMinorUnits);
        SpendingValueRules.Units(quantity, unitPrice);
        if (IsAdditionalCost && (amount <= 0 || quantity.HasValue || unitPrice.HasValue))
            throw new InvalidValueException("Additional costs must be positive amounts without quantity or unit price.");
        if (quantity.HasValue && UnitKey is null)
            throw new InvalidValueException("Quantity needs a saved unit.");
        if (!Enum.IsDefined(promotionStatus)) throw new InvalidValueException("Select a valid promotion status.");
        var savedNotes = SpendingSetupRules.Notes(notes);
        NetAmount = amount;
        Quantity = quantity;
        UnitPrice = unitPrice;
        PromotionStatus = promotionStatus;
        Notes = savedNotes;
    }

    public decimal? SuggestedAmount => Quantity.HasValue
        ? decimal.Round(Quantity.Value * UnitPrice!.Value, CurrencyMinorUnits, MidpointRounding.AwayFromZero) : null;
    public decimal? EffectiveUnitPrice => Quantity.HasValue ? NetAmount / Quantity.Value : null;
}
