using Hearthcast.Api.Projections.Contracts;
using Hearthcast.Data.Projections.Calculation;
using Hearthcast.Data.Projections.Preparation;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Projections;

[ApiController]
[Route("api/households/{householdId:guid}/snapshots/{snapshotId:guid}/projection")]
public sealed class SnapshotProjectionsController(ProjectionPreparationService preparation) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ProjectionRunResponse>> Run(Guid householdId, Guid snapshotId, RunProjectionRequest request,
        CancellationToken cancellationToken)
    {
        var p = request.PresetId is { } presetId
            ? await preparation.PrepareSavedPresetAsync(householdId, snapshotId, presetId, cancellationToken)
            : await preparation.PrepareAsync(householdId, snapshotId, request.Inputs!.Assumptions!.ToSettings(),
                request.Inputs.ToGroups(), request.Inputs.ToAssets(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = p.CanCalculate ? ProjectionCalculator.Calculate(p.Assumptions, p.StartingPosition!) : null;
        cancellationToken.ThrowIfCancellationRequested();
        return ProjectionRunResponse.From(p, result);
    }
}
