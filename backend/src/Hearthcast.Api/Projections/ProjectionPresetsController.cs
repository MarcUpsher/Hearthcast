using Hearthcast.Api.Projections.Contracts;
using Hearthcast.Data.Projections;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Projections;

[ApiController]
[Route("api/households/{householdId:guid}/projection-presets")]
public sealed class ProjectionPresetsController(ProjectionPresetService presets) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectionPresetResponse>>> List(Guid householdId, CancellationToken cancellationToken) =>
        (await presets.ListAsync(householdId, cancellationToken)).Select(ProjectionPresetResponse.From).ToArray();

    [HttpGet("{presetId:guid}")]
    public async Task<ActionResult<ProjectionPresetResponse>> Get(Guid householdId, Guid presetId, CancellationToken cancellationToken) =>
        ProjectionPresetResponse.From(await presets.GetAsync(householdId, presetId, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ProjectionPresetResponse>> Create(Guid householdId, SaveProjectionPresetRequest request, CancellationToken cancellationToken)
    {
        var input = request.Inputs!;
        var result = await presets.CreateAsync(householdId, input.Assumptions!.ToSettings(request.Name!), input.ToGroups(), input.ToAssets(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, presetId = result.Id }, ProjectionPresetResponse.From(result));
    }

    [HttpPut("{presetId:guid}")]
    public async Task<ActionResult<ProjectionPresetResponse>> Update(Guid householdId, Guid presetId, SaveProjectionPresetRequest request, CancellationToken cancellationToken)
    {
        var input = request.Inputs!;
        return ProjectionPresetResponse.From(await presets.UpdateAsync(householdId, presetId,
            input.Assumptions!.ToSettings(request.Name!), input.ToGroups(), input.ToAssets(), cancellationToken));
    }

    [HttpDelete("{presetId:guid}")]
    public async Task<IActionResult> Delete(Guid householdId, Guid presetId, CancellationToken cancellationToken)
    {
        await presets.DeleteAsync(householdId, presetId, cancellationToken);
        return NoContent();
    }
}
