using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.HistoryImport;

internal sealed class PreparedHistory
{
    public required HistoryTables Tables { get; init; }
    public required Household Household { get; init; }
    public required Dictionary<string, Person> People { get; init; }
    public required Dictionary<string, Category> Categories { get; init; }
    public required Dictionary<string, ItemGroup> Groups { get; init; }
    public required Dictionary<string, TrackedItem> Items { get; init; }
    public required Dictionary<string, FinancedAsset> Assets { get; init; }
    public required Dictionary<string, Snapshot> Snapshots { get; init; }

    public static PreparedHistory Create(HistoryFile file)
    {
        Require(file.FormatVersion == 2 && file.ImportKind == "HistoricalSnapshots", "Expected HistoricalSnapshots format version 2.");
        var t = file.Tables;
        Require(t.Households.Length == 1 && t.Snapshots.Length > 0, "Supply one household with at least one snapshot.");
        var home = t.Households.Single();
        Require(!string.IsNullOrWhiteSpace(home.HouseholdRef), "HouseholdRef is required.");
        var personRows = Index(t.People, r => r.PersonRef);
        Require(t.People.All(r => r.HouseholdRef == home.HouseholdRef), "People must belong to the imported household.");
        var first = t.People.First(r => r.ParticipatesInHouseholdFinances);
        var household = Household.Create(home.Name, first.DisplayName);
        var people = new Dictionary<string, Person> { [first.PersonRef] = household.People.Single() };
        foreach (var row in t.People.Where(r => r != first))
            people.Add(row.PersonRef, household.AddPerson(row.DisplayName, row.ParticipatesInHouseholdFinances));
        var categories = Index(t.Categories, r => r.CategoryRef).ToDictionary(p => p.Key, p =>
        {
            Require(p.Value.HouseholdRef == home.HouseholdRef, "Category belongs to another household.");
            return new Category(household, p.Value.Name, p.Value.FinancialType);
        });
        var groups = Index(t.Groups, r => r.GroupRef).ToDictionary(p => p.Key,
            p => new ItemGroup(categories[p.Value.CategoryRef], p.Value.Name));
        var itemRows = Index(t.Items, r => r.ItemRef);
        var items = itemRows.ToDictionary(p => p.Key, p => new TrackedItem(groups[p.Value.GroupRef],
            people[p.Value.HolderPersonRef], p.Value.Name, p.Value.CurrencyCode, p.Value.CountryCode));
        var assetRows = Index(t.FinancedAssets, r => r.AssetRef);
        var assets = assetRows.ToDictionary(p => p.Key, p => new FinancedAsset(groups[p.Value.GroupRef], p.Value.Name,
            p.Value.CurrencyCode, p.Value.CountryCode, p.Value.PurchaseDate, p.Value.PurchasePrice,
            p.Value.OriginalLoanAmount, p.Value.HasWithdrawalFacility));
        var snapshotRows = Index(t.Snapshots, r => r.SnapshotRef);
        Require(t.Snapshots.All(r => r.HouseholdRef == home.HouseholdRef), "Snapshot belongs to another household.");
        Require(t.Snapshots.Select(r => (r.SnapshotDate, r.DatePrecision)).Distinct().Count() == t.Snapshots.Length,
            "Duplicate snapshot date/precision.");
        var itemValues = t.ItemValues.ToLookup(r => r.SnapshotRef);
        var assetValues = t.AssetValues.ToLookup(r => r.SnapshotRef);
        Require(t.ItemValues.All(r => snapshotRows.ContainsKey(r.SnapshotRef) && items.ContainsKey(r.ItemRef))
            && t.AssetValues.All(r => snapshotRows.ContainsKey(r.SnapshotRef) && assets.ContainsKey(r.AssetRef)), "Value has an unknown reference.");
        foreach (var a in t.Associations)
        {
            Require(people.ContainsKey(a.PersonRef), "Association has unknown person.");
            Require(a.TargetType == "Item" ? items.ContainsKey(a.TargetRef) && a.Role == "Household"
                : a.TargetType == "FinancedAsset" && assets.ContainsKey(a.TargetRef) && a.Role is "Owner" or "LoanHolder" or "Household",
                "Association has invalid target or role.");
            Require(a.Role == "LoanHolder" ? a.ShareBasisPoints is null : a.ShareBasisPoints is not null, "Association share missing or inappropriate.");
            if (a.SnapshotRef is not null)
                Require(snapshotRows.ContainsKey(a.SnapshotRef) && (a.TargetType == "Item"
                    ? itemValues[a.SnapshotRef].Any(r => r.ItemRef == a.TargetRef)
                    : assetValues[a.SnapshotRef].Any(r => r.AssetRef == a.TargetRef)), "Association override has no snapshot entry.");
        }
        Require(t.Associations.Select(a => (a.TargetType,a.TargetRef,a.SnapshotRef,a.Role,a.PersonRef)).Distinct().Count() == t.Associations.Length,
            "Duplicate association person.");
        var associations = t.Associations.ToLookup(a => (a.TargetType,a.TargetRef,a.SnapshotRef,a.Role));
        AssociationRow[] Rows(string kind, string id, string? period, string role) =>
            associations.Contains((kind,id,period,role)) ? associations[(kind,id,period,role)].ToArray()
                : associations[(kind,id,null,role)].ToArray();
        (Person, int)[] Shares(string kind, string id, string? period, string role) =>
            Rows(kind,id,period,role).Select(a => (people[a.PersonRef], a.ShareBasisPoints!.Value)).ToArray();
        void ConfigureItem(string id, string? period) => items[id].SetHouseholdAllocation(itemRows[id].IncludeInHousehold,
            Shares("Item",id,period,"Household"));
        void ConfigureAsset(string id, string? period) => assets[id].SetPersonAssociations(
            Shares("FinancedAsset",id,period,"Owner"), Rows("FinancedAsset",id,period,"LoanHolder").Select(a => people[a.PersonRef]).ToArray(),
            assetRows[id].IncludeInHousehold, Shares("FinancedAsset",id,period,"Household"));
        foreach (var id in items.Keys) ConfigureItem(id,null);
        foreach (var id in assets.Keys) ConfigureAsset(id,null);
        var snapshots = new Dictionary<string, Snapshot>();
        Snapshot? previous = null;
        string? previousRef = null;
        foreach (var row in t.Snapshots.OrderBy(r => r.SnapshotDate))
        {
            try
            {
                var values = itemValues[row.SnapshotRef].ToArray();
                var balances = assetValues[row.SnapshotRef].ToArray();
                Require(values.Select(r => r.ItemRef).Distinct().Count() == values.Length, "Duplicate item value.");
                foreach (var v in values)
                {
                    ConfigureItem(v.ItemRef,row.SnapshotRef);
                    items[v.ItemRef].Rename(v.Name ?? itemRows[v.ItemRef].Name);
                    Require(v.ValueOrigin != SnapshotValueOrigin.CarriedForward || v.CarryFromSnapshotRef == previousRef && previous is not null,
                        "Carry must reference the previous supplied snapshot.");
                }
                foreach (var group in balances.GroupBy(r => r.AssetRef))
                {
                    Require(group.Select(r => r.BalanceKind).ToHashSet().SetEquals(Enum.GetValues<FinancedAssetBalanceKind>())
                        && group.Count() == 3, "Supply all three financed balances exactly once.");
                    ConfigureAsset(group.Key,row.SnapshotRef);
                }
                Require(balances.All(v => v.ValueOrigin != SnapshotValueOrigin.CarriedForward
                    || v.CarryFromSnapshotRef == previousRef && previous is not null), "Invalid financed carry source.");
                var snapshot = new Snapshot(household,row.SnapshotDate,row.DatePrecision,
                    values.Select(v => items[v.ItemRef]).ToArray(),
                    values.Where(v => v.ValueOrigin != SnapshotValueOrigin.CarriedForward).Select(v => new ItemSnapshotValue(items[v.ItemRef].Id,v.Amount)).ToArray(),
                    balances.Select(v => assets[v.AssetRef]).Distinct().ToArray(),
                    balances.Where(v => v.ValueOrigin != SnapshotValueOrigin.CarriedForward)
                        .Select(v => new FinancedAssetSnapshotValue(assets[v.AssetRef].Id,v.BalanceKind,v.Amount)).ToArray(), previous);
                Require(snapshot.SnapshotDate == row.SnapshotDate, "Snapshot date is not canonical for its precision.");
                snapshots.Add(row.SnapshotRef,snapshot);
                previous = snapshot;
                previousRef = row.SnapshotRef;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidDataException($"Snapshot '{row.SnapshotRef}' could not be constructed: {ex.Message}",ex);
            }
        }
        // All captures copied their metadata. Persist only final defaults in the registry.
        foreach (var (id,item) in items) { ConfigureItem(id,null); item.Rename(itemRows[id].Name); item.SetRetired(itemRows[id].IsRetired); }
        foreach (var (id,asset) in assets) { ConfigureAsset(id,null); asset.SetRetired(assetRows[id].IsRetired); }
        return new() { Tables=t,Household=household,People=people,Categories=categories,Groups=groups,Items=items,Assets=assets,Snapshots=snapshots };
    }

    private static Dictionary<string,T> Index<T>(IEnumerable<T> rows, Func<T,string> key)
    {
        var result = new Dictionary<string,T>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var id = key(row);
            Require(!string.IsNullOrWhiteSpace(id) && id == id.Trim(), "References must be nonempty and trimmed.");
            Require(result.TryAdd(id,row), $"Duplicate reference '{id}'.");
        }
        return result;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
