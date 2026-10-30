using Hearthcast.Api.Classification.Contracts;
using Hearthcast.Data.Classification;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Classification;

[ApiController]
[Route("api/households/{householdId:guid}/categories")]
public sealed class CategoriesController(ClassificationService classification) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CategoryResponse>> Create(Guid householdId, CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await classification.CreateCategoryAsync(householdId, request.Name!, request.FinancialType!.Value, cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, categoryId = result.Id }, CategoryResponse.From(result));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> List(Guid householdId, CancellationToken cancellationToken) =>
        (await classification.ListCategoriesAsync(householdId, cancellationToken)).Select(CategoryResponse.From).ToArray();

    [HttpGet("{categoryId:guid}")]
    public async Task<ActionResult<CategoryResponse>> Get(Guid householdId, Guid categoryId, CancellationToken cancellationToken) =>
        CategoryResponse.From(await classification.GetCategoryAsync(householdId, categoryId, cancellationToken));

    [HttpPut("{categoryId:guid}")]
    public async Task<ActionResult<CategoryResponse>> Rename(Guid householdId, Guid categoryId, RenameCategoryRequest request,
        CancellationToken cancellationToken) =>
        CategoryResponse.From(await classification.RenameCategoryAsync(householdId, categoryId, request.Name!, cancellationToken));
}
