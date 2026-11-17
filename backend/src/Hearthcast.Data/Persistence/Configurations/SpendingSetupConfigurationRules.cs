using Hearthcast.Data.Spending;

namespace Hearthcast.Data.Persistence.Configurations;

internal static class SpendingSetupConfigurationRules
{
    internal const string UsualPriceIndex = "UX_SpendingPrices_ItemMerchantCurrencyUnit";

    // Column names are fixed mapping identifiers, never user input.
    internal static string CanonicalText(string column)
    {
        var characters = string.Concat(SpendingSetupRules.TrimCharacters.Select(c => $"\\{(int)c:X4}"));
        return $"length(\"{column}\") > 0 AND \"{column}\" = btrim(\"{column}\", U&'{characters}')";
    }
}
