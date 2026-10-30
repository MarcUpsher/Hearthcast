using Hearthcast.Api.Classification.Contracts;
using Hearthcast.Data.Classification;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Classification;

[ApiController]
[Route("api/households/{householdId:guid}/categories/{categoryId:guid}/groups")]
public sealed class GroupsController(ClassificationService classification) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GroupResponse>> Create(Guid householdId, Guid categoryId, CreateGroupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await classification.CreateGroupAsync(householdId, categoryId, request.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, categoryId, groupId = result.Id }, GroupResponse.From(result));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GroupResponse>>> List(Guid householdId, Guid categoryId, CancellationToken cancellationToken) =>
        (await classification.ListGroupsAsync(householdId, categoryId, cancellationToken)).Select(GroupResponse.From).ToArray();

    [HttpGet("{groupId:guid}")]
    public async Task<ActionResult<GroupResponse>> Get(Guid householdId, Guid categoryId, Guid groupId, CancellationToken cancellationToken) =>
        GroupResponse.From(await classification.GetGroupAsync(householdId, categoryId, groupId, cancellationToken));

    [HttpPut("{groupId:guid}")]
    public async Task<ActionResult<GroupResponse>> Rename(Guid householdId, Guid categoryId, Guid groupId, RenameGroupRequest request,
        CancellationToken cancellationToken) =>
        GroupResponse.From(await classification.RenameGroupAsync(householdId, categoryId, groupId, request.Name!, cancellationToken));
}
