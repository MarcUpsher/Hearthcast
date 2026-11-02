using System.Net;
using System.Net.Http.Json;
using System.Text;
using Hearthcast.Api.FinancedAssets.Contracts;
using Hearthcast.Api.Snapshots.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Api.TrackedItems.Contracts;
using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Api.Tests.Renaming;

public sealed class ItemRenameApiTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private ApiTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new ApiTestFactory(_database);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenamePreservesIdentitySetupAndHistoryAndUsesNewNameForFutureCarries(bool financed)
    {
        var (home,id,path) = await Setup(financed);
        var snapshots = new SnapshotService(_database);
        var original = await snapshots.CreateAsync(home,new DateOnly(2023,1,1),
            financed ? [] : [new(id,100m)],DatePrecision.Month,
            financed ? [new(id,FinancedAssetBalanceKind.CurrentValuation,300m),new(id,FinancedAssetBalanceKind.OutstandingLoanBalance,200m)] : []);
        Assert.Equal(HttpStatusCode.OK,(await _client.PutAsJsonAsync(path+"/retirement",new { isRetired=true })).StatusCode);
        var before = await _client.GetStringAsync(path);
        var response = await _client.PutAsJsonAsync(path+"/name",new { name="  Renamed  " });
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        if (financed)
        {
            var old = System.Text.Json.JsonSerializer.Deserialize<FinancedAssetResponse>(before,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            var renamed = (await response.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
            Assert.Equivalent(old with { Name="Renamed" },renamed);
            Assert.True(renamed.IsRetired);
            using var independent = _factory.CreateClient();
            Assert.Equivalent(renamed,await independent.GetFromJsonAsync<FinancedAssetResponse>(path));
        }
        else
        {
            var old = System.Text.Json.JsonSerializer.Deserialize<TrackedItemResponse>(before,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            var renamed = (await response.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
            Assert.Equivalent(old with { Name="Renamed" },renamed);
            Assert.True(renamed.IsRetired);
            using var independent = _factory.CreateClient();
            Assert.Equivalent(renamed,await independent.GetFromJsonAsync<TrackedItemResponse>(path));
        }
        Assert.Equal(HttpStatusCode.OK,(await _client.PutAsJsonAsync(path+"/name",new { name="Renamed" })).StatusCode);
        var other = await new HouseholdService(_database).CreateAsync("Other","Other person");
        var segment=financed?"financed-assets":"tracked-items";
        Assert.Equal(HttpStatusCode.NotFound,(await _client.PutAsJsonAsync($"/api/households/{other.Id}/{segment}/{id}/name",new { name="Wrong household" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await _client.PutAsJsonAsync($"/api/households/{home}/{segment}/{Guid.NewGuid()}/name",new { name="Missing" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await _client.PutAsJsonAsync(path+"/retirement",new { isRetired=false })).StatusCode);
        var next = await snapshots.CreateNextAsync(home,new DateOnly(2023,2,1),datePrecision:DatePrecision.Month,sourceSnapshotId:original.Id);
        var saved = await snapshots.GetAsync(home,original.Id);
        Assert.Equal(original.Revision,saved.Revision);
        if (financed)
        {
            Assert.Equal("Original",Assert.Single(saved.FinancedAssetSnapshots).AssetName);
            var entry=Assert.Single(next.FinancedAssetSnapshots);
            Assert.Equal(id,entry.FinancedAssetId);
            Assert.Equal("Renamed",entry.AssetName);
            Assert.Equal(300m,entry.CurrentValuation);
            Assert.Equal(SnapshotValueOrigin.CarriedForward,entry.CurrentValuationValueOrigin);
        }
        else
        {
            Assert.Equal("Original",Assert.Single(saved.ItemSnapshots).ItemName);
            var entry=Assert.Single(next.ItemSnapshots);
            Assert.Equal(id,entry.TrackedItemId);
            Assert.Equal("Renamed",entry.ItemName);
            Assert.Equal(100m,entry.Amount);
            Assert.Equal(SnapshotValueOrigin.CarriedForward,entry.ValueOrigin);
        }
    }

    [Theory]
    [InlineData(false,"{}")]
    [InlineData(true,"{}")]
    [InlineData(false,"{\"name\":null}")]
    [InlineData(true,"{\"name\":null}")]
    [InlineData(false,"{\"name\":\"\"}")]
    [InlineData(true,"{\"name\":\"\"}")]
    [InlineData(false,"{\"name\":\"  \"}")]
    [InlineData(true,"{\"name\":\"  \"}")]
    [InlineData(false,"{\"name\":\"Changed\",\"isRetired\":true}")]
    [InlineData(true,"{\"name\":\"Changed\",\"originalLoanAmount\":0}")]
    public async Task InvalidRenameLeavesNameAndSetupUnchanged(bool financed,string json)
    {
        var (_,_,path)=await Setup(financed);
        var before=await _client.GetStringAsync(path);
        var response=await _client.PutAsync(path+"/name",new StringContent(json,Encoding.UTF8,"application/json"));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Equal(before,await _client.GetStringAsync(path));
    }

    private async Task<(Guid Home,Guid Id,string Path)> Setup(bool financed)
    {
        var households=new HouseholdService(_database);
        var home=await households.CreateAsync("Example","Alex");
        var second=await households.CreatePersonAsync(home.Id,"Morgan",true);
        var first=home.People.Single().Id;
        var classification=new ClassificationService(_database);
        var category=await classification.CreateCategoryAsync(home.Id,"Assets",FinancialType.Asset);
        var group=await classification.CreateGroupAsync(home.Id,category.Id,"Holdings");
        Guid id;
        if(financed)
            id=(await new FinancedAssetService(_database).CreateAsync(home.Id,group.Id,"Original","GBP","GB",
                new DateOnly(2020,1,1),400m,250m,true,[new(first,5000),new(second.Id,5000)],[second.Id],true,[new(second.Id,10000)])).Id;
        else
            id=(await new TrackedItemService(_database).CreateAsync(home.Id,group.Id,first,"Original","GBP","GB",true,
                [new(first,5000),new(second.Id,5000)])).Id;
        return (home.Id,id,$"/api/households/{home.Id}/{(financed?"financed-assets":"tracked-items")}/{id}");
    }
}
