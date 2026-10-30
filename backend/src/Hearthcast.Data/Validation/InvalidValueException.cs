namespace Hearthcast.Data.Validation;

/// <summary>A supplied domain value fails validation.</summary>
public sealed class InvalidValueException(string message, string? parameterName = null)
    : ArgumentException(message, parameterName);
