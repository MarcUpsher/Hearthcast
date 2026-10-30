using Hearthcast.Api.TrackedItems.Contracts;
using Hearthcast.Data.TrackedItems;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.TrackedItems;

[ApiController]
[Route("api/households/{householdId:guid}/tracked-items")]
public sealed class TrackedItemsController(TrackedItemService items) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TrackedItemResponse>> Create(Guid householdId, CreateTrackedItemRequest request,
        CancellationToken cancellationToken)
    {
        var allocations = request.Allocations?.Select(share =>
            new TrackedItemAllocationShare(share!.PersonId!.Value, share.ShareBasisPoints!.Value)).ToArray();
        var item = await items.CreateAsync(householdId, request.GroupId!.Value, request.HolderPersonId!.Value,
            request.Name!, request.CurrencyCode!, request.CountryCode!, request.IncludeInHousehold!.Value,
            allocations, cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, trackedItemId = item.Id }, TrackedItemResponse.From(item));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TrackedItemResponse>>> List(Guid householdId,
        CancellationToken cancellationToken) =>
        (await items.ListAsync(householdId, cancellationToken)).Select(TrackedItemResponse.From).ToArray();

    [HttpGet("{trackedItemId:guid}")]
    public async Task<ActionResult<TrackedItemResponse>> Get(Guid householdId, Guid trackedItemId,
        CancellationToken cancellationToken) =>
        TrackedItemResponse.From(await items.GetAsync(householdId, trackedItemId, cancellationToken));

    [HttpPut("{trackedItemId:guid}/household-allocation")]
    public async Task<ActionResult<TrackedItemResponse>> SetHouseholdAllocation(Guid householdId, Guid trackedItemId,
        SetHouseholdAllocationRequest request, CancellationToken cancellationToken)
    {
        var allocations = request.Allocations?.Select(share =>
            new TrackedItemAllocationShare(share!.PersonId!.Value, share.ShareBasisPoints!.Value)).ToArray();
        return TrackedItemResponse.From(await items.SetHouseholdAllocationAsync(householdId, trackedItemId,
            request.IncludeInHousehold!.Value, allocations, cancellationToken));
    }

    [HttpPut("{trackedItemId:guid}/retirement")]
    public async Task<ActionResult<TrackedItemResponse>> SetRetirement(Guid householdId, Guid trackedItemId,
        SetRetirementRequest request, CancellationToken cancellationToken) =>
        TrackedItemResponse.From(await items.SetRetiredAsync(householdId, trackedItemId,
            request.IsRetired!.Value, cancellationToken));

}
