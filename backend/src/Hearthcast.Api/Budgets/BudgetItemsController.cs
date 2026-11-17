using Hearthcast.Api.Budgets.Contracts;
using Hearthcast.Data.Budgets;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Budgets;

[ApiController]
[Route("api/households/{householdId:guid}/budgets/items")]
public sealed class BudgetItemsController(BudgetSetupService setup) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BudgetItemResponse>> Create(Guid householdId, CreateBudgetItemRequest request,
        CancellationToken cancellationToken)
    {
        var item = await setup.CreateItemAsync(householdId, request.ExpenseCategoryId!.Value, request.Code!, request.Name!,
            request.CurrencyCode!, request.Frequency!.Value, request.IntervalCount!.Value, request.MerchantId, request.Notes, cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, itemId = item.Id }, BudgetItemResponse.From(item));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BudgetItemResponse>>> List(Guid householdId,
        CancellationToken cancellationToken, [FromQuery] bool includeRetired = true) =>
        (await setup.ListItemsAsync(householdId, includeRetired, cancellationToken)).Select(BudgetItemResponse.From).ToArray();

    [HttpGet("{itemId:guid}")]
    public async Task<ActionResult<BudgetItemResponse>> Get(Guid householdId, Guid itemId, CancellationToken cancellationToken) =>
        BudgetItemResponse.From(await setup.GetItemAsync(householdId, itemId, cancellationToken));

    [HttpPut("{itemId:guid}/details")]
    public async Task<ActionResult<BudgetItemResponse>> Update(Guid householdId, Guid itemId,
        UpdateBudgetItemRequest request, CancellationToken cancellationToken) =>
        BudgetItemResponse.From(await setup.UpdateItemAsync(householdId, itemId, request.ExpenseCategoryId!.Value,
            request.Name!, request.CurrencyCode!, request.Frequency!.Value, request.IntervalCount!.Value,
            request.MerchantId, request.Notes, cancellationToken));

    [HttpPut("{itemId:guid}/retirement")]
    public async Task<ActionResult<BudgetItemResponse>> SetRetirement(Guid householdId, Guid itemId,
        BudgetRetirementRequest request, CancellationToken cancellationToken) =>
        BudgetItemResponse.From(await setup.SetItemRetiredAsync(householdId, itemId, request.IsRetired!.Value, cancellationToken));
}
