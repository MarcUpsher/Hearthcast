namespace Hearthcast.Data.Validation;

/// <summary>A requested domain resource does not exist in the specified scope.</summary>
public sealed class ResourceNotFoundException(string message) : KeyNotFoundException(message);
