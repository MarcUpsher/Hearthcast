using Hearthcast.Data.Classification;
using Hearthcast.Data.Projections.Calculation;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Projections.Preparation;

internal static class ProjectionSnapshotAdapter
{
    public static ProjectionPreparation Prepare(Snapshot snapshot, ProjectionPresetSettings assumptions,
        IReadOnlyCollection<ProjectionGroupSelection> selections, IReadOnlyCollection<ProjectionFinancedAssetSettings> loans)
    {
        var groups = selections.ToDictionary(g => g.GroupId, g => g.Bucket);
        var assets = loans.ToDictionary(a => a.FinancedAssetId);
        var issues = new List<ProjectionPreparationIssue>();
        var coverage = new List<ProjectionCoverageEntry>();
        var preparedAssets = new List<ProjectionFinancedAssetInput>();
        var balances = assumptions.Currencies.ToDictionary(c => c.CurrencyCode, _ => (Investments: 0m, Savings: 0m));
        var missingCurrencies = new HashSet<string>();
        var invalidGroups = new HashSet<Guid>();
        foreach (var selection in selections.OrderBy(g => g.GroupId))
        {
            var items = snapshot.ItemSnapshots.Where(i => i.GroupId == selection.GroupId).ToArray();
            if (!items.Any() && !snapshot.FinancedAssetSnapshots.Any(a => a.GroupId == selection.GroupId))
                Error(ProjectionPreparationIssueKind.MissingGroup, selection.GroupId, "A selected group is absent from this snapshot.");
            else if (items.Any(i => i.FinancialType != FinancialType.Asset))
            {
                invalidGroups.Add(selection.GroupId);
                Error(ProjectionPreparationIssueKind.DebtGroupSelected, selection.GroupId, "Investment and savings selections require Asset groups in this snapshot.");
            }
            else if (!items.Any(i => i.IncludeInHousehold))
                Error(ProjectionPreparationIssueKind.NoHouseholdItems, selection.GroupId, "A selected group has no ordinary household items in this snapshot.");
        }
        foreach (var item in snapshot.ItemSnapshots.OrderBy(i => i.ItemName, StringComparer.Ordinal).ThenBy(i => i.Id))
        {
            var selected = groups.TryGetValue(item.GroupId, out var bucket);
            var disposition = !item.IncludeInHousehold ? ProjectionCoverageDisposition.ExcludedFromHousehold
                : !selected ? ProjectionCoverageDisposition.NotSelected
                : invalidGroups.Contains(item.GroupId) ? ProjectionCoverageDisposition.InvalidSelection
                : bucket == ProjectionBucket.Investment ? ProjectionCoverageDisposition.Investment : ProjectionCoverageDisposition.Savings;
            var carried = item.ValueOrigin == SnapshotValueOrigin.CarriedForward;
            coverage.Add(new(item.Id, item.TrackedItemId, ProjectionCoverageKind.Item, item.GroupId, item.GroupName,
                item.CategoryName, item.ItemName, item.CurrencyCode, item.FinancialType, disposition, item.Amount, null, carried));
            if (disposition is not (ProjectionCoverageDisposition.Investment or ProjectionCoverageDisposition.Savings)) continue;
            if (carried) Carried(item.TrackedItemId, item.ItemName);
            if (item.Amount is null)
                Error(ProjectionPreparationIssueKind.MissingValue, item.TrackedItemId, $"{item.ItemName} has no known balance.");
            var configured = balances.TryGetValue(item.CurrencyCode, out var native);
            if (!configured) Error(ProjectionPreparationIssueKind.MissingCurrencySettings, item.TrackedItemId,
                $"Configure a {item.CurrencyCode} currency pot for {item.ItemName}.");
            var rate = Rate(item.CurrencyCode);
            if (item.Amount is { } amount && rate is { } factor)
            {
                _ = Convert(amount, factor);
                if (configured) balances[item.CurrencyCode] = bucket == ProjectionBucket.Investment
                    ? (native.Investments + amount, native.Savings) : (native.Investments, native.Savings + amount);
            }
        }
        foreach (var asset in snapshot.FinancedAssetSnapshots.OrderBy(a => a.AssetName, StringComparer.Ordinal).ThenBy(a => a.Id))
        {
            var selected = assets.TryGetValue(asset.FinancedAssetId, out var settings);
            var disposition = !asset.IncludeInHousehold ? ProjectionCoverageDisposition.ExcludedFromHousehold
                : selected ? ProjectionCoverageDisposition.FinancedAsset : ProjectionCoverageDisposition.NotSelected;
            var carried = asset.CurrentValuationValueOrigin == SnapshotValueOrigin.CarriedForward
                || asset.OutstandingLoanBalanceValueOrigin == SnapshotValueOrigin.CarriedForward;
            coverage.Add(new(asset.Id, asset.FinancedAssetId, ProjectionCoverageKind.FinancedAsset, asset.GroupId,
                asset.GroupName, asset.CategoryName, asset.AssetName, asset.CurrencyCode, asset.FinancialType, disposition,
                asset.CurrentValuation, asset.OutstandingLoanBalance, carried));
            if (!selected) continue;
            if (!asset.IncludeInHousehold)
            {
                Error(ProjectionPreparationIssueKind.ExcludedFinancedAsset, asset.FinancedAssetId, $"{asset.AssetName} is excluded from the household in this snapshot.");
                continue;
            }
            if (carried) Carried(asset.FinancedAssetId, asset.AssetName);
            if (asset.CurrentValuation is null)
                Error(ProjectionPreparationIssueKind.MissingValue, asset.FinancedAssetId, $"{asset.AssetName} has no known valuation.");
            if (asset.OutstandingLoanBalance is null)
                Error(ProjectionPreparationIssueKind.MissingValue, asset.FinancedAssetId, $"{asset.AssetName} has no known outstanding loan balance.");
            var rate = Rate(asset.CurrencyCode);
            if (asset.CurrentValuation is { } value && asset.OutstandingLoanBalance is { } debt && rate is { } factor)
            {
                // Retain native inputs, but reject conversions that would lose a balance.
                _ = Convert(value, factor);
                _ = Convert(debt, factor);
                preparedAssets.Add(new(asset.CurrencyCode, factor, value, debt, settings!));
            }
        }
        foreach (var asset in loans.OrderBy(a => a.FinancedAssetId))
            if (!snapshot.FinancedAssetSnapshots.Any(a => a.FinancedAssetId == asset.FinancedAssetId))
                Error(ProjectionPreparationIssueKind.MissingFinancedAsset, asset.FinancedAssetId, "A selected financed asset is absent from this snapshot; its loan cannot be assumed paid off.");

        var currencyPositions = new List<ProjectionCurrencyPosition>();
        foreach (var pot in assumptions.Currencies.OrderBy(c => c.CurrencyCode))
        {
            var native = balances[pot.CurrencyCode];
            var used = native.Investments != 0 || native.Savings != 0 || pot.MonthlyInvestmentContribution != 0
                || pot.MonthlySavingsContribution != 0 || loans.Any(l => l.InvestmentCurrencyAfterPayoff == pot.CurrencyCode
                    && (l.MonthlyLoanPaymentInAssetCurrency != 0 || l.ExtraMonthlyLoanPaymentInAssetCurrency != 0));
            var rate = Rate(pot.CurrencyCode, used);
            if (rate is { } factor)
            {
                _ = Convert(native.Investments, factor);
                _ = Convert(native.Savings, factor);
            }
            currencyPositions.Add(new(pot.CurrencyCode, native.Investments, native.Savings, rate));
        }
        var expenseRate = assumptions.ExpenseTarget is { } target ? Rate(target.CurrencyCode) : null;
        ProjectionStartingPosition? starting = issues.Any(i => i.Severity == ProjectionIssueSeverity.Error) ? null
            : new(snapshot.SnapshotDate, currencyPositions.AsReadOnly(), preparedAssets.AsReadOnly(), expenseRate);
        return new(snapshot.Id, snapshot.Revision, assumptions, starting, issues.AsReadOnly(), coverage.AsReadOnly());

        void Error(ProjectionPreparationIssueKind kind, Guid? id, string message) =>
            issues.Add(new(kind, ProjectionIssueSeverity.Error, id, message));
        void Carried(Guid id, string name) => issues.Add(new(ProjectionPreparationIssueKind.CarriedValue,
            ProjectionIssueSeverity.Warning, id, $"{name} uses carried-forward values."));
        decimal? BaseRate(string currency) => currency == snapshot.ExchangeRateBaseCurrency ? 1m
            : snapshot.ExchangeRates.SingleOrDefault(r => r.BaseCurrency == snapshot.ExchangeRateBaseCurrency && r.QuoteCurrency == currency)?.Rate;
        decimal? Rate(string currency, bool required = true)
        {
            if (currency == assumptions.CurrencyCode) return 1;
            if (BaseRate(currency) is { } source && BaseRate(assumptions.CurrencyCode) is { } target)
            {
                var factor = target / source;
                if (factor <= 0) throw new InvalidValueException("The saved exchange rate exceeds supported numeric precision.");
                return factor;
            }
            if (required && missingCurrencies.Add(currency)) Error(ProjectionPreparationIssueKind.MissingExchangeRate, null,
                $"No saved conversion from {currency} to {assumptions.CurrencyCode} is available in this snapshot.");
            return null;
        }
    }

    private static decimal Convert(decimal amount, decimal factor)
    {
        var converted = amount * factor;
        if (amount != 0 && converted == 0) throw new InvalidValueException("The converted starting balance exceeds supported numeric precision.");
        return converted;
    }
}
