using System.Globalization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Data.Tests.Budgets;

public sealed class BudgetCreateRequestTests
{
    private static readonly Guid Household = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Snapshot = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Source = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Item = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Category = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Merchant = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid SourceEntry = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static BudgetSnapshotValues Values() => new(new(2026, 11, 1),
        [new(Item, BudgetEntryValueMode.Confirm, 200.000m, new(2026, 10, 30), new(Source, SourceEntry),
            Metadata: new(" energy ", " Energy ", Category, " Bills ", " gbp ", BudgetFrequency.Month, 1, Merchant, " Supplier ", " \t "))],
        Source, " Notes ");

    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    public void VersionOneHashMatchesAnIndependentlyCalculatedGoldenFixture(string culture)
    {
        // Independently calculated using ordered compact JSON and SHA-256. Keep compatible with saved requests.
        const string expected = "c616712ba2822dc77dc2da8bfc465edad07a50df63c4f04908cce6af323359f9";
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(expected, BudgetCreateRequest.Freeze(Household, Snapshot, Values()).Hash);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void OrderWhitespaceCaseAndDecimalScaleCanonicaliseButExplicitIntentRemainsPartOfTheHash()
    {
        var values = Values();
        var second = new BudgetEntryValues(Guid.Parse("88888888-8888-8888-8888-888888888888"), BudgetEntryValueMode.Unrecorded);
        BudgetEntryValues[] entries = [second, values.Entries[0]];
        var original = BudgetCreateRequest.Freeze(Household, Snapshot, values with { Entries = entries });
        var normalised = BudgetCreateRequest.Freeze(Household, Snapshot, values with
        {
            Notes = "Notes", Entries = [values.Entries[0] with { PlannedAmount = 200, Metadata = original.Values.Entries[0].Metadata }, second]
        });
        Assert.Equal(original.Hash, normalised.Hash);
        entries[1] = second;
        Assert.Equal(Item, original.Values.Entries[0].BudgetItemId);
        Assert.NotEqual(original.Hash, BudgetCreateRequest.Freeze(Household, Snapshot,
            values with { Entries = [values.Entries[0], second with { Mode = BudgetEntryValueMode.Confirm, PlannedAmount = 0, ConfirmedOn = new(2026, 1, 1) }] }).Hash);
        Assert.NotEqual(BudgetCreateRequest.Freeze(Household, Snapshot, values with { Entries = [] }).Hash,
            BudgetCreateRequest.Freeze(Household, Snapshot, values with { Entries = [], CopiedFromBudgetSnapshotId = null }).Hash);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("date")]
    [InlineData("infiniteDate")]
    [InlineData("template")]
    [InlineData("selfTemplate")]
    [InlineData("selfSource")]
    [InlineData("duplicate")]
    [InlineData("nullEntries")]
    [InlineData("nullEntry")]
    [InlineData("item")]
    [InlineData("mode")]
    [InlineData("amountMissing")]
    [InlineData("confirmationMissing")]
    [InlineData("amountNegative")]
    [InlineData("amountBound")]
    [InlineData("amountPrecision")]
    [InlineData("confirmationSentinel")]
    [InlineData("unrecordedAmount")]
    [InlineData("carryAmount")]
    [InlineData("sourceId")]
    [InlineData("sourceEntry")]
    [InlineData("conflictingMetadata")]
    [InlineData("currency")]
    [InlineData("interval")]
    [InlineData("frequency")]
    [InlineData("category")]
    [InlineData("merchantPair")]
    [InlineData("code")]
    [InlineData("notes")]
    public void InvalidRequestShapesCannotBeFrozen(string field)
    {
        var values = Values();
        var entry = values.Entries[0];
        var metadata = entry.Metadata!;
        entry = field switch
        {
            "item" => entry with { BudgetItemId = Guid.Empty },
            "mode" => entry with { Mode = (BudgetEntryValueMode)0 },
            "amountMissing" => entry with { PlannedAmount = null },
            "confirmationMissing" => entry with { ConfirmedOn = null },
            "amountNegative" => entry with { PlannedAmount = -1 },
            "amountBound" => entry with { PlannedAmount = 1_000_000_000_000m },
            "amountPrecision" => entry with { PlannedAmount = 0.0000001m },
            "confirmationSentinel" => entry with { ConfirmedOn = DateOnly.MaxValue },
            "unrecordedAmount" => entry with { Mode = BudgetEntryValueMode.Unrecorded },
            "carryAmount" => entry with { Mode = BudgetEntryValueMode.Carry },
            "sourceId" => entry with { Source = new(Guid.Empty, SourceEntry) },
            "sourceEntry" => entry with { Source = new(Source, Guid.Empty) },
            "selfSource" => entry with { Source = new(Snapshot, SourceEntry) },
            "conflictingMetadata" => entry with { RefreshFromSetup = true },
            "currency" => entry with { Metadata = metadata with { CurrencyCode = "GB1" } },
            "interval" => entry with { Metadata = metadata with { IntervalCount = 0 } },
            "frequency" => entry with { Metadata = metadata with { Frequency = (BudgetFrequency)0 } },
            "category" => entry with { Metadata = metadata with { ExpenseCategoryId = Guid.Empty } },
            "merchantPair" => entry with { Metadata = metadata with { MerchantName = null } },
            "code" => entry with { Metadata = metadata with { ItemCode = "bad code" } },
            "notes" => entry with { Metadata = metadata with { Notes = "bad\0notes" } },
            _ => entry
        };
        values = values with { Entries = [entry] };
        values = field switch
        {
            "date" => values with { EffectiveFrom = DateOnly.MinValue },
            "infiniteDate" => values with { EffectiveFrom = DateOnly.MaxValue },
            "template" => values with { CopiedFromBudgetSnapshotId = Guid.Empty },
            "selfTemplate" => values with { CopiedFromBudgetSnapshotId = Snapshot },
            "duplicate" => values with { Entries = [entry, entry] },
            "nullEntries" => values with { Entries = null! },
            "nullEntry" => values with { Entries = [null!] },
            _ => values
        };
        Assert.ThrowsAny<ArgumentException>(() => BudgetCreateRequest.Freeze(Household, field == "id" ? Guid.Empty : Snapshot, values));
    }
}
