using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Households.Contracts;

public sealed record RenameHouseholdRequest
{
    [Required]
    public string? Name { get; init; }
}
