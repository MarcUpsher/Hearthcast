using Hearthcast.Data.Validation;
using Hearthcast.Api.Households.Contracts;
using Hearthcast.Data.Households;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Households;

[ApiController]
[Route("api/households")]
public sealed class HouseholdsController(HouseholdService households) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<HouseholdResponse>> Create(CreateHouseholdRequest request, CancellationToken cancellationToken)
    {
        var household = await households.CreateAsync(request.Name!, request.FirstPersonDisplayName!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId = household.Id }, HouseholdResponse.From(household));
    }

    [HttpGet("{householdId:guid}")]
    public async Task<ActionResult<HouseholdResponse>> Get(Guid householdId, CancellationToken cancellationToken) =>
        HouseholdResponse.From(await households.GetAsync(householdId, cancellationToken));

    [HttpPut("{householdId:guid}")]
    public async Task<ActionResult<HouseholdResponse>> Rename(Guid householdId, RenameHouseholdRequest request,
        CancellationToken cancellationToken) =>
        HouseholdResponse.From(await households.RenameAsync(householdId, request.Name!, cancellationToken));

    [HttpPost("{householdId:guid}/people")]
    public async Task<ActionResult<PersonResponse>> CreatePerson(Guid householdId, CreatePersonRequest request,
        CancellationToken cancellationToken)
    {
        var person = await households.CreatePersonAsync(householdId, request.DisplayName!,
            request.ParticipatesInHouseholdFinances!.Value, cancellationToken);
        return CreatedAtAction(nameof(GetPerson), new { householdId, personId = person.Id }, PersonResponse.From(person));
    }

    [HttpGet("{householdId:guid}/people/{personId:guid}")]
    public async Task<ActionResult<PersonResponse>> GetPerson(Guid householdId, Guid personId, CancellationToken cancellationToken)
    {
        var household = await households.GetAsync(householdId, cancellationToken);
        var person = household.People.SingleOrDefault(person => person.Id == personId)
            ?? throw new ResourceNotFoundException("The person does not belong to this household.");
        return PersonResponse.From(person);
    }

    [HttpPut("{householdId:guid}/people/{personId:guid}")]
    public async Task<ActionResult<PersonResponse>> UpdatePerson(Guid householdId, Guid personId, UpdatePersonRequest request,
        CancellationToken cancellationToken)
    {
        var household = await households.UpdatePersonAsync(householdId, personId, request.DisplayName!,
            request.ParticipatesInHouseholdFinances!.Value, cancellationToken);
        return PersonResponse.From(household.People.Single(person => person.Id == personId));
    }

    [HttpDelete("{householdId:guid}/people/{personId:guid}")]
    public async Task<IActionResult> RemovePerson(Guid householdId, Guid personId, CancellationToken cancellationToken)
    {
        await households.RemovePersonAsync(householdId, personId, cancellationToken);
        return NoContent();
    }
}
