using Hearthcast.Api.Spending.Contracts;
using Hearthcast.Data.Spending;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Spending;

[ApiController]
[Route("api/households/{householdId:guid}/spending/categories")]
public sealed class ExpenseCategoriesController(SpendingSetupService setup) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ExpenseCategoryResponse>> Create(Guid householdId, SpendingNameRequest request,
        CancellationToken cancellationToken)
    {
        var category = await setup.CreateCategoryAsync(householdId, request.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, categoryId = category.Id }, ExpenseCategoryResponse.From(category));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExpenseCategoryResponse>>> List(Guid householdId,
        CancellationToken cancellationToken, [FromQuery] bool includeRetired = true) =>
        (await setup.ListCategoriesAsync(householdId, includeRetired, cancellationToken)).Select(ExpenseCategoryResponse.From).ToArray();

    [HttpGet("{categoryId:guid}")]
    public async Task<ActionResult<ExpenseCategoryResponse>> Get(Guid householdId, Guid categoryId,
        CancellationToken cancellationToken) =>
        ExpenseCategoryResponse.From(await setup.GetCategoryAsync(householdId, categoryId, cancellationToken));

    [HttpPut("{categoryId:guid}/name")]
    public async Task<ActionResult<ExpenseCategoryResponse>> Rename(Guid householdId, Guid categoryId,
        SpendingNameRequest request, CancellationToken cancellationToken) =>
        ExpenseCategoryResponse.From(await setup.RenameCategoryAsync(householdId, categoryId, request.Name!, cancellationToken));

    [HttpPut("{categoryId:guid}/retirement")]
    public async Task<ActionResult<ExpenseCategoryResponse>> SetRetirement(Guid householdId, Guid categoryId,
        SpendingRetirementRequest request, CancellationToken cancellationToken) =>
        ExpenseCategoryResponse.From(await setup.SetCategoryRetiredAsync(householdId, categoryId, request.IsRetired!.Value, cancellationToken));
}
