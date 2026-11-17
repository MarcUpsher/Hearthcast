using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingSetupTests
{
    [Theory]
    [InlineData(65, 1)]
    [InlineData(1, 33)]
    [InlineData(4096, 1)]
    [InlineData(1, 4096)]
    public void OverlengthIdentityStringsFailBeforePersistence(int codeLength, int unitLength)
    {
        var category = new ExpenseCategory(Household.Create("Home", "Alex"), "Food");
        Assert.Throws<InvalidValueException>(() => new SpendingItem(category, SpendingSetupTestText.Key(codeLength),
            "Eggs", SpendingSetupTestText.Key(unitLength), "Each"));
    }

    [Fact]
    public void MaximumIdentityLengthsAreAcceptedAfterTrimming()
    {
        var category = new ExpenseCategory(Household.Create("Home", "Alex"), "Food");
        var item = new SpendingItem(category, $" {SpendingSetupTestText.Key(64)} ", "Eggs",
            $"\t{SpendingSetupTestText.Key(32)}\n", "Each");
        Assert.Equal(64, item.Code.Length);
        Assert.Equal(32, item.UnitKey.Length);
    }

    [Fact]
    public void ExplicitWhitespacePolicyNormalizesDescriptionsAndPreservesInternalWhitespace()
    {
        // Verify the explicit policy against this target runtime rather than assuming Unicode equivalence.
        var runtimeWhitespace = new string(Enumerable.Range(0, char.MaxValue + 1).Select(i => (char)i)
            .Where(char.IsWhiteSpace).ToArray());
        Assert.Equal(SpendingSetupRules.TrimCharacters, runtimeWhitespace);
        var home = Household.Create("Home", "Alex");
        foreach (var c in SpendingSetupRules.TrimCharacters)
        {
            var category = new ExpenseCategory(home, $"{c}Food{c}");
            var merchant = new Merchant(home, $"{c}Shop{c}");
            var item = new SpendingItem(category, "EGGS", $"{c}Eggs{c}", "each", $"{c}Each{c}", $"{c}Notes{c}");
            Assert.Equal("Food", category.Name);
            Assert.Equal("Shop", merchant.Name);
            Assert.Equal("Eggs", item.Name);
            Assert.Equal("Each", item.UnitLabel);
            Assert.Equal("Notes", item.Notes);
            item.SetNotes(c.ToString());
            Assert.Null(item.Notes);
            Assert.Throws<InvalidValueException>(() => category.Rename(c.ToString()));
        }
        Assert.Equal("Food\tand\ncoffee", SpendingSetupRules.Text("Food\tand\ncoffee"));
        Assert.Equal("\u200BFood\u200B", SpendingSetupRules.Text("\u200BFood\u200B"));
    }

    [Fact]
    public void SetupNormalizesIdentityWithoutInferringCategoryOrCurrencyMeaning()
    {
        var home = Household.Create("Home", "Alex");
        var category = new ExpenseCategory(home, " Food ");
        var merchant = new Merchant(home, " Shop ");
        var item = new SpendingItem(category, " eggs-6 ", " Eggs ", " PACK-6 ", " Six eggs ", " Notes ");
        var price = new SpendingItemMerchantPrice(item, merchant, " zar ", 3.123456m);
        Assert.Equal(home.Id, item.HouseholdId);
        Assert.Equal(category.Id, item.ExpenseCategoryId);
        Assert.Equal("Food", category.Name);
        Assert.Equal("Shop", merchant.Name);
        Assert.Equal("EGGS-6", item.Code);
        Assert.Equal("pack-6", item.UnitKey);
        Assert.Equal("Six eggs", item.UnitLabel);
        Assert.Equal("Notes", item.Notes);
        Assert.Equal("ZAR", price.CurrencyCode);
        Assert.Equal(item.UnitKey, price.UnitKey);
        Assert.Equal(item.UnitLabel, price.UnitLabel);
        Assert.NotEqual(Guid.Empty, price.Revision);
        Assert.Equal(TimeSpan.Zero, price.UpdatedAtUtc.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    [InlineData("bad\0name")]
    public void RequiredTextCannotBeMissingOrContainNullCharacters(string? text)
    {
        var home = Household.Create("Home", "Alex");
        var category = new ExpenseCategory(home, "Food");
        Assert.Throws<InvalidValueException>(() => new ExpenseCategory(home, text!));
        Assert.Throws<InvalidValueException>(() => new Merchant(home, text!));
        Assert.Throws<InvalidValueException>(() => new SpendingItem(category, "EGGS", text!, "each", "Each"));
        Assert.Throws<InvalidValueException>(() => new SpendingItem(category, "EGGS", "Eggs", "each", text!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("two words")]
    [InlineData("_eggs")]
    [InlineData("éggs")]
    [InlineData("egg/6")]
    public void IdentityCodesAndUnitsRejectAmbiguousKeys(string text)
    {
        var category = new ExpenseCategory(Household.Create("Home", "Alex"), "Food");
        Assert.Throws<InvalidValueException>(() => new SpendingItem(category, text, "Eggs", "each", "Each"));
        Assert.Throws<InvalidValueException>(() => new SpendingItem(category, "EGGS", "Eggs", text, "Each"));
    }

    [Theory]
    [InlineData("-0.000001")]
    [InlineData("1000000000")]
    [InlineData("0.0000001")]
    public void InvalidPriceDoesNotPartiallyMutateTheRememberedPrice(string value)
    {
        var home = Household.Create("Home", "Alex");
        var item = new SpendingItem(new(home, "Food"), "EGGS", "Eggs", "each", "Each");
        var price = new SpendingItemMerchantPrice(item, new(home, "Shop"), "GBP", 3m);
        var revision = price.Revision;
        var updated = price.UpdatedAtUtc;
        var invalid = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<InvalidValueException>(() => price.SetUsualUnitPrice(invalid));
        Assert.Equal(3m, price.UsualUnitPrice);
        Assert.Equal(revision, price.Revision);
        Assert.Equal(updated, price.UpdatedAtUtc);
        Assert.Throws<InvalidValueException>(() => new SpendingItemMerchantPrice(item, new(home, "Shop"), "GBP", invalid));
    }

    [Fact]
    public void CrossHouseholdAssociationsAreRejectedBeforeMutatingState()
    {
        var home = Household.Create("Home", "Alex");
        var category = new ExpenseCategory(home, "Food");
        var other = Household.Create("Other", "Blair");
        var item = new SpendingItem(category, "EGGS", "Eggs", "each", "Each");
        Assert.Throws<InvalidValueException>(() => item.SetCategory(new(other, "Food")));
        Assert.Equal(category.Id, item.ExpenseCategoryId);
        Assert.Throws<InvalidValueException>(() => new SpendingItemMerchantPrice(item, new(other, "Shop"), "GBP", 3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("GB")]
    [InlineData("GB1")]
    [InlineData("G B")]
    public void PriceCurrencyMustBeAnExplicitThreeLetterCode(string? code)
    {
        var home = Household.Create("Home", "Alex");
        var item = new SpendingItem(new(home, "Food"), "EGGS", "Eggs", "each", "Each");
        Assert.ThrowsAny<ArgumentException>(() => new SpendingItemMerchantPrice(item, new(home, "Shop"), code!, 3));
    }

    [Fact]
    public void RenamingRetiringAndEditingDescriptionsPreserveStableIdentitiesAndPrices()
    {
        var home = Household.Create("Home", "Alex");
        var category = new ExpenseCategory(home, "Food");
        var merchant = new Merchant(home, "Shop");
        var item = new SpendingItem(category, "EGGS", "Eggs", "pack-6", "Six eggs");
        var price = new SpendingItemMerchantPrice(item, merchant, "GBP", 3);
        var itemId = item.Id;
        category.Rename("Groceries");
        merchant.Rename("New shop name");
        item.Rename("Large eggs");
        item.SetUnitLabel("Box of six");
        item.SetNotes("  ");
        category.SetRetired(true);
        merchant.SetRetired(true);
        item.SetRetired(true);
        Assert.Equal(itemId, item.Id);
        Assert.Equal("EGGS", item.Code);
        Assert.Equal("pack-6", item.UnitKey);
        Assert.Equal("Six eggs", price.UnitLabel);
        Assert.Equal(3m, price.UsualUnitPrice);
        Assert.Null(item.Notes);
        Assert.True(category.IsRetired && merchant.IsRetired && item.IsRetired);
        var revision = price.Revision;
        price.SetUsualUnitPrice(0);
        Assert.NotEqual(revision, price.Revision);
        Assert.Equal(0, price.UsualUnitPrice);
    }
}
