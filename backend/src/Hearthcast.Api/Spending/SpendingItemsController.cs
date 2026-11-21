using Hearthcast.Api.Spending.Contracts;
using Hearthcast.Data.Spending;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Spending;

[ApiController]
[Route("api/households/{householdId:guid}/spending/items")]
public sealed class SpendingItemsController(SpendingSetupService setup) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SpendingItemResponse>> Create(Guid householdId, CreateSpendingItemRequest request,
        CancellationToken cancellationToken)
    {
        var item = await setup.CreateItemAsync(householdId, request.ExpenseCategoryId!.Value, request.Code!, request.Name!,
            request.UnitKey!, request.UnitLabel!, request.Notes, cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, itemId = item.Id }, SpendingItemResponse.From(item));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SpendingItemResponse>>> List(Guid householdId,
        CancellationToken cancellationToken, [FromQuery] bool includeRetired = true) =>
        (await setup.ListItemsAsync(householdId, includeRetired, cancellationToken)).Select(SpendingItemResponse.From).ToArray();

    [HttpGet("{itemId:guid}")]
    public async Task<ActionResult<SpendingItemResponse>> Get(Guid householdId, Guid itemId,
        CancellationToken cancellationToken) =>
        SpendingItemResponse.From(await setup.GetItemAsync(householdId, itemId, cancellationToken));

    [HttpPut("{itemId:guid}/details")]
    public async Task<ActionResult<SpendingItemResponse>> Update(Guid householdId, Guid itemId,
        UpdateSpendingItemRequest request, CancellationToken cancellationToken) =>
        SpendingItemResponse.From(await setup.UpdateItemAsync(householdId, itemId, request.ExpenseCategoryId!.Value,
            request.Code, request.Name!, request.UnitLabel!, request.Notes, cancellationToken));

    [HttpPut("{itemId:guid}/retirement")]
    public async Task<ActionResult<SpendingItemResponse>> SetRetirement(Guid householdId, Guid itemId,
        SpendingRetirementRequest request, CancellationToken cancellationToken) =>
        SpendingItemResponse.From(await setup.SetItemRetiredAsync(householdId, itemId, request.IsRetired!.Value, cancellationToken));

    [HttpGet("{itemId:guid}/usual-prices")]
    public async Task<ActionResult<IReadOnlyList<UsualPriceResponse>>> ListPrices(Guid householdId, Guid itemId,
        CancellationToken cancellationToken) =>
        (await setup.ListUsualPricesAsync(householdId, itemId, cancellationToken)).Select(UsualPriceResponse.From).ToArray();

    [HttpGet("{itemId:guid}/usual-price")]
    public async Task<ActionResult<UsualPriceResponse>> FindPrice(Guid householdId, Guid itemId,
        [FromQuery] FindUsualPriceRequest request, CancellationToken cancellationToken)
    {
        var price = await setup.FindUsualPriceAsync(householdId, itemId, request.MerchantId!.Value,
            request.CurrencyCode!, cancellationToken);
        return price is null ? NoContent() : Ok(UsualPriceResponse.From(price));
    }
}
