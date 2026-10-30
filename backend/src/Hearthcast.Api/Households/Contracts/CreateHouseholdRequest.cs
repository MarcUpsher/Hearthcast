using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Households.Contracts;

public sealed record CreateHouseholdRequest
{
    [Required]
    public string? Name { get; init; }

    [Required]
    public string? FirstPersonDisplayName { get; init; }
}
