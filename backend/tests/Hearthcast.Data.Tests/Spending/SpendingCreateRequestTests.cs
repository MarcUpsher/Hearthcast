using System.Globalization;
using Hearthcast.Data.Spending;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingCreateRequestTests
{
    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    public void VersionOneCreateHashMatchesThePersistedCompatibilityFixture(string culture)
    {
        // Independently computed from the version-one canonical JSON, not from Freeze.
        // Do not update this value to accommodate a serializer/property-order refactor:
        // old saved create identities must continue to recognise their original requests.
        const string expectedHash = "583b3ef7470f7b817eedadf4b8c3ee3529e32e0ac29f653bf5aca619f2fbe301";
        var household = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var record = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var merchant = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var firstCategory = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var firstItem = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var secondCategory = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var secondItem = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var priceRevision = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var values = new SpendingRecordValues(merchant, new(2026, 11, 17), " gbp ",
        [
            new(firstCategory, 6.00m, firstItem, 2.000m, 3.00m, Description: " Eggs ",
                ReceiptDescription: " EGGS SIX ", MerchantItemReference: " E6 ", Notes: "\t "),
            new(secondCategory, 2.500m, secondItem, 1.00m, 2.500m, PromotionStatus: PromotionStatus.Yes, Notes: " Sale ")
        ], ReceiptTotal: 8.500m, ExternalReference: " Receipt 42 ", Notes: " ");
        RememberSpendingPrice[] prices = [new(2, 2.500m), new(1, 3.00m, priceRevision)];
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var frozen = SpendingCreateRequest.Freeze(household, record, values, prices);
            Assert.Equal(expectedHash, frozen.Hash);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
