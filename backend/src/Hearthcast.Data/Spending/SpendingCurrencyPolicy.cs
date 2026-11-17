using System.Collections.Frozen;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

/// <summary>Explicit receipt precision, independent of locale/cash rounding and exchange rates.</summary>
public static class SpendingCurrencyPolicy
{
    // SIX ISO 4217 List One, published 2026-09-17. Only entries with numeric minor units.
    // https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists/list-one.xml
    public const string SourceVersion = "ISO4217-2026-09-17";
    public static IReadOnlyDictionary<string, int> SupportedCurrencies { get; } = Build();

    private static FrozenDictionary<string, int> Build()
    {
        var groups = new Dictionary<int, string>
        {
            [0] = "BIF CLP DJF GNF ISK JPY KMF KRW PYG RWF UGX UYI VND VUV XAF XOF XPF",
            [2] = "AED AFN ALL AMD AOA ARS AUD AWG AZN BAM BBD BDT BMD BND BOB BOV BRL BSD BTN BWP BYN BZD CAD CDF CHE CHF CHW CNY COP COU CRC CUP CVE CZK DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS GIP GMD GTQ GYD HKD HNL HTG HUF IDR ILS INR IRR JMD KES KGS KHR KPW KYD KZT LAK LBP LKR LRD LSL MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN MXV MYR MZN NAD NGN NIO NOK NPR NZD PAB PEN PGK PHP PKR PLN QAR RON RSD RUB SAR SBD SCR SDG SEK SGD SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TOP TRY TTD TWD TZS UAH USD USN UYU UZS VED VES WST XAD XCD XCG YER ZAR ZMW ZWG",
            [3] = "BHD IQD JOD KWD LYD OMR TND",
            [4] = "CLF UYW"
        };
        return groups.SelectMany(g => g.Value.Split(' ').Select(code => KeyValuePair.Create(code, g.Key)))
            .ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static string Normalize(string code)
    {
        var normalized = SpendingSetupRules.Text(code).ToUpperInvariant();
        if (!SupportedCurrencies.ContainsKey(normalized))
            throw new InvalidValueException("This currency has no supported receipt precision.", nameof(code));
        return normalized;
    }

    public static int MinorUnits(string code) => SupportedCurrencies[Normalize(code)];
}
