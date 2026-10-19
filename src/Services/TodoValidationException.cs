namespace TodoMcp.Services;

/// <summary>Raised when caller-supplied input is rejected; the message is safe to show to the caller.</summary>
public sealed class TodoValidationException(string message) : Exception(message);
