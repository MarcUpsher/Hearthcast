using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Households.Contracts;

public sealed record CreatePersonRequest
{
    [Required]
    public string? DisplayName { get; init; }

    [Required]
    public bool? ParticipatesInHouseholdFinances { get; init; }
}
