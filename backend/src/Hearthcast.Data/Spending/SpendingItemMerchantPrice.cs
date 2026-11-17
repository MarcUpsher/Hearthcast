using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

public sealed class SpendingItemMerchantPrice
{
    private SpendingItemMerchantPrice() { }

    internal SpendingItemMerchantPrice(SpendingItem item, Merchant merchant, string currencyCode, decimal usualUnitPrice)
    {
        if (item.HouseholdId != merchant.HouseholdId)
            throw new InvalidValueException("The item and merchant must belong to the same household.", nameof(merchant));
        Id = Guid.NewGuid();
        HouseholdId = item.HouseholdId;
        SpendingItemId = item.Id;
        MerchantId = merchant.Id;
        CurrencyCode = RequiredAlphabeticCode.Normalize(currencyCode, 3);
        UnitKey = item.UnitKey;
        UnitLabel = item.UnitLabel;
        SetUsualUnitPrice(usualUnitPrice);
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid SpendingItemId { get; private set; }
    public Guid MerchantId { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public string UnitKey { get; private set; } = string.Empty;
    public string UnitLabel { get; private set; } = string.Empty;
    public decimal UsualUnitPrice { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid Revision { get; private set; }
    public Guid? SourceSpendingLineId { get; private set; }

    public void SetUsualUnitPrice(decimal price)
    {
        var valid = SpendingSetupRules.UsualPrice(price);
        UsualUnitPrice = valid;
        SourceSpendingLineId = null;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        Revision = Guid.NewGuid();
    }

    internal void RememberFrom(SpendingRecord record, SpendingLine line, decimal chosenUnitPrice)
    {
        if (record.HouseholdId != HouseholdId || record.Kind != SpendingRecordKind.Payment || record.IsVoided
            || line.SpendingRecordId != record.Id || !record.Lines.Contains(line)
            || line.SpendingItemId != SpendingItemId || record.MerchantId != MerchantId
            || record.CurrencyCode != CurrencyCode || line.UnitKey != UnitKey || line.Quantity is null || line.UnitPrice is null)
            throw new InvalidValueException("A usual-price source must be a compatible itemised payment line.");
        SetUsualUnitPrice(chosenUnitPrice);
        SourceSpendingLineId = line.Id;
    }

    internal void ClearSource()
    {
        if (SourceSpendingLineId is null) return;
        SourceSpendingLineId = null;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        Revision = Guid.NewGuid();
    }
}
