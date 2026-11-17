using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingRecordTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    public void CreateIdentityRequiresANonemptyIdAndCanonicalHash(string? hash)
    {
        var d = Setup();
        Assert.Throws<InvalidValueException>(() => new SpendingRecord(Guid.NewGuid(), d.Merchant, new(2026, 1, 1),
            "GBP", [new(d.Food, 1m)], hash!));
        Assert.Throws<InvalidValueException>(() => new SpendingRecord(Guid.Empty, d.Merchant, new(2026, 1, 1),
            "GBP", [new(d.Food, 1m)], new string('a', 64)));
    }

    private static (Merchant Merchant, ExpenseCategory Food, ExpenseCategory Coffee, SpendingItem Item) Setup()
    {
        var home = Household.Create("Home", "Alex");
        var food = new ExpenseCategory(home, "Food");
        return (new Merchant(home, "Shop"), food, new(home, "Coffee"), new(food, "EGGS-6", "Eggs", "pack-6", "Six eggs"));
    }

    private static SpendingRecord Create(Merchant merchant, IReadOnlyList<SpendingLineInput> lines, string currency = "GBP",
        SpendingRecordKind kind = SpendingRecordKind.Payment, SpendingRecord? original = null, decimal? receiptTotal = null) =>
        new(Guid.NewGuid(), merchant, new(2026, 11, 17), currency, lines, new string('a', 64), kind, original, receiptTotal);

    [Fact]
    public void QuickSplitAndDetailedEntriesShareOneTotalWithoutNeedingCatalogueItems()
    {
        var d = Setup();
        var quick = Create(d.Merchant, [new(d.Food, 12.50m)]);
        Assert.Null(Assert.Single(quick.Lines).SpendingItemId);
        Assert.Equal(12.50m, quick.TotalAmount);
        Assert.Equal("NotChecked", quick.ReconciliationStatus);
        var revision = quick.Revision;
        var old = quick.ReplaceLines(revision, [new(d.Coffee, 7m, Description: "Coffee"), new(d.Food, 5.50m, Description: "Scones")]);
        Assert.Single(old);
        Assert.Equal(12.50m, quick.TotalAmount);
        Assert.Equal(new[] { 1, 2 }, quick.Lines.Select(l => l.Position));
        Assert.NotEqual(revision, quick.Revision);
        var detailed = Create(d.Merchant, [new(d.Food, 5m, d.Item, 2m, 3m, PromotionStatus: PromotionStatus.Yes),
            new(d.Food, 3m, d.Item, 1m, 3m), new(d.Coffee, 2.50m, Quantity: 1m, UnitPrice: 2.50m, UnitKey: "cup", UnitLabel: "Cup")]);
        Assert.Equal(10.50m, detailed.TotalAmount);
        Assert.Equal(6m, detailed.Lines[0].SuggestedAmount);
        Assert.Equal(2.5m, detailed.Lines[0].EffectiveUnitPrice);
        Assert.Equal(PromotionStatus.Yes, detailed.Lines[0].PromotionStatus);
        Assert.Equal(PromotionStatus.Unknown, detailed.Lines[1].PromotionStatus);
    }

    [Theory]
    [InlineData("JPY", 0, "1", "0.5")]
    [InlineData("GBP", 2, "0.01", "0.005")]
    [InlineData("KWD", 3, "0.001", "0.0005")]
    [InlineData("CLF", 4, "0.0001", "0.00005")]
    public void CurrencyPrecisionIsExplicitAndSuggestedAmountsRoundAwayFromZero(string currency, int digits, string valid, string invalid)
    {
        var d = Setup();
        decimal Parse(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        var amount = Parse(valid);
        var record = Create(d.Merchant, [new(d.Food, amount, d.Item, 0.5m, amount)], currency);
        Assert.Equal(digits, record.CurrencyMinorUnits);
        Assert.Equal(digits, record.Lines[0].CurrencyMinorUnits);
        Assert.Equal(amount, record.Lines[0].SuggestedAmount);
        Assert.Throws<InvalidValueException>(() => Create(d.Merchant, [new(d.Food, Parse(invalid))], currency));
        Assert.Throws<InvalidValueException>(() => Create(d.Merchant, [new(d.Food, amount)], currency, receiptTotal: Parse(invalid)));
    }

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("XXX")]
    [InlineData("XAU")]
    [InlineData("GB")]
    public void UnsupportedReceiptCurrenciesAreRejectedRatherThanAssumedToHaveTwoDecimals(string currency)
    {
        var d = Setup();
        Assert.Throws<InvalidValueException>(() => Create(d.Merchant, [new(d.Food, 1)], currency));
    }

    [Fact]
    public void MetadataIsCopiedAndAmountCorrectionsPreserveHistoricalContext()
    {
        var d = Setup();
        var record = Create(d.Merchant, [new(d.Food, 3m, d.Item, 1m, 3m)]);
        d.Merchant.Rename("New merchant");
        d.Food.Rename("New category");
        d.Item.Rename("New item");
        d.Item.SetUnitLabel("New label");
        d.Merchant.SetRetired(true);
        d.Item.SetRetired(true);
        var revision = record.Revision;
        record.CorrectLine(revision, record.Lines[0].Id, 2m, 1m, 2m, PromotionStatus.Unknown, "Correction");
        Assert.Equal("Shop", record.MerchantName);
        Assert.Equal("Food", record.Lines[0].ExpenseCategoryName);
        Assert.Equal("Eggs", record.Lines[0].ItemName);
        Assert.Equal("Six eggs", record.Lines[0].UnitLabel);
        Assert.NotEqual(revision, record.Revision);
    }

    [Fact]
    public void ReceiptMismatchIsAllowedAndRefundVoidingDoesNotInventOrEraseAPayment()
    {
        var d = Setup();
        var payment = Create(d.Merchant, [new(d.Food, 10m)], receiptTotal: 12m);
        Assert.Equal("Unreconciled", payment.ReconciliationStatus);
        Assert.Equal(2m, payment.ReceiptDifference);
        payment.CorrectHeader(payment.Revision, new(2026, 11, 1), 10m, " Receipt ", " Notes ");
        Assert.Equal("Reconciled", payment.ReconciliationStatus);
        Assert.Equal("Receipt", payment.ExternalReference);
        var refund = Create(d.Merchant, [new(d.Food, 2m)], kind: SpendingRecordKind.Refund, original: payment);
        Assert.Equal(-2m, refund.CountedAmount);
        Assert.Equal(payment.Id, refund.OriginalSpendingRecordId);
        var revision = payment.Revision;
        payment.Void(revision);
        Assert.NotEqual(revision, payment.Revision);
        Assert.Equal(0m, payment.CountedAmount);
        Assert.Equal(-2m, refund.CountedAmount);
        Assert.Single(payment.Lines);
        Assert.Throws<BusinessRuleViolationException>(() => payment.ReplaceLines(payment.Revision, [new(d.Food, 0m)]));
        Assert.Throws<InvalidValueException>(() => Create(d.Merchant, [new(d.Food, 1m)], kind: SpendingRecordKind.Refund, original: payment));
    }

    [Fact]
    public void InvalidMutationsAndStaleRevisionsLeaveTheAggregateUnchanged()
    {
        var d = Setup();
        var record = Create(d.Merchant, [new(d.Food, 3m)]);
        var revision = record.Revision;
        var originalLine = record.Lines[0];
        Assert.Throws<InvalidValueException>(() => record.ReplaceLines(revision, []));
        Assert.Throws<InvalidValueException>(() => record.ReplaceLines(revision, [new(d.Food, 1m), new(d.Food, -1m)]));
        Assert.Throws<InvalidValueException>(() => record.CorrectHeader(revision, new(2026, 1, 1), 4m, "Reference", "bad\0notes"));
        Assert.Throws<InvalidValueException>(() => record.CorrectLine(revision, originalLine.Id, 5m, 1m, null, PromotionStatus.Yes, null));
        Assert.Throws<SpendingRecordConflictException>(() => record.ReplaceLines(Guid.NewGuid(), [new(d.Food, 0m)]));
        Assert.Throws<SpendingRecordConflictException>(() => record.Void(Guid.NewGuid()));
        Assert.Equal(revision, record.Revision);
        Assert.Same(originalLine, Assert.Single(record.Lines));
        Assert.Equal(3m, record.TotalAmount);
        Assert.Null(record.ExternalReference);
        Assert.Null(record.ReceiptTotal);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("quantityZero")]
    [InlineData("quantityPrecision")]
    [InlineData("quantityBound")]
    [InlineData("priceNegative")]
    [InlineData("pricePrecision")]
    [InlineData("priceBound")]
    [InlineData("pair")]
    [InlineData("unit")]
    [InlineData("promotion")]
    public void InvalidLineValuesCannotCreateARecord(string error)
    {
        var d = Setup();
        var input = new SpendingLineInput(d.Food, 1m, d.Item, 1m, 1m);
        input = error switch
        {
            "amount" => input with { NetAmount = 1_000_000_000_000m },
            "quantityZero" => input with { Quantity = 0 },
            "quantityPrecision" => input with { Quantity = 0.0000001m },
            "quantityBound" => input with { Quantity = 1_000_000m },
            "priceNegative" => input with { UnitPrice = -1 },
            "pricePrecision" => input with { UnitPrice = 0.0000001m },
            "priceBound" => input with { UnitPrice = 1_000_000_000m },
            "pair" => input with { UnitPrice = null },
            "unit" => input with { Item = null },
            _ => input with { PromotionStatus = (PromotionStatus)0 }
        };
        Assert.Throws<InvalidValueException>(() => Create(d.Merchant, [input]));
    }

    [Fact]
    public void CrossHouseholdReferencesAndInvalidRefundTargetsAreRejected()
    {
        var d = Setup();
        var other = Setup();
        Assert.Throws<InvalidValueException>(() => Create(d.Merchant, [new(other.Food, 1m)]));
        Assert.Throws<InvalidValueException>(() => Create(d.Merchant, [new(d.Food, 1m, other.Item)]));
        var payment = Create(d.Merchant, [new(d.Food, 1m)]);
        var refund = Create(d.Merchant, [new(d.Food, 0m)], kind: SpendingRecordKind.Refund);
        Assert.Throws<InvalidValueException>(() => Create(d.Merchant, [new(d.Food, 1m)], original: payment));
        Assert.Throws<InvalidValueException>(() => Create(d.Merchant, [new(d.Food, 1m)], kind: SpendingRecordKind.Refund, original: refund));
        Assert.Throws<InvalidValueException>(() => Create(other.Merchant, [new(other.Food, 1m)], kind: SpendingRecordKind.Refund, original: payment));
        Assert.Throws<InvalidValueException>(() => new SpendingRecord(payment.Id, d.Merchant, new(2026, 1, 1), "GBP",
            [new(d.Food, 1m)], new string('a', 64), SpendingRecordKind.Refund, payment));
    }

    [Fact]
    public void ExplicitPriceSourcesRequireCompatibleItemisedPaymentsAndNeverUpdateAutomatically()
    {
        var d = Setup();
        var price = new SpendingItemMerchantPrice(d.Item, d.Merchant, "GBP", 3m);
        var record = Create(d.Merchant, [new(d.Food, 2m, d.Item, 1m, 2m)]);
        Assert.Equal(3m, price.UsualUnitPrice);
        price.RememberFrom(record, record.Lines[0], 2m);
        Assert.Equal(record.Lines[0].Id, price.SourceSpendingLineId);
        record.CorrectLine(record.Revision, record.Lines[0].Id, 1m, 1m, 1m, PromotionStatus.Yes, null);
        Assert.Equal(2m, price.UsualUnitPrice);
        var revision = price.Revision;
        price.ClearSource();
        Assert.Null(price.SourceSpendingLineId);
        Assert.NotEqual(revision, price.Revision);
        Assert.Equal(2m, price.UsualUnitPrice);
        var quick = Create(d.Merchant, [new(d.Food, 2m, d.Item)]);
        Assert.Throws<InvalidValueException>(() => price.RememberFrom(quick, quick.Lines[0], 2m));
        var refund = Create(d.Merchant, [new(d.Food, 2m, d.Item, 1m, 2m)], kind: SpendingRecordKind.Refund);
        Assert.Throws<InvalidValueException>(() => price.RememberFrom(refund, refund.Lines[0], 2m));
        record.Void(record.Revision);
        Assert.Throws<InvalidValueException>(() => price.RememberFrom(record, record.Lines[0], 2m));
    }
}
