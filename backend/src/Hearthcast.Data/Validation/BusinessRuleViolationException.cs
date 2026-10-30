namespace Hearthcast.Data.Validation;

/// <summary>An operation conflicts with the current domain state.</summary>
public sealed class BusinessRuleViolationException(string message) : InvalidOperationException(message);
