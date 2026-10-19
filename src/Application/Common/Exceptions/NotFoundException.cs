namespace Mondje.Application.Common.Exceptions;

/// <summary>The requested resource does not exist. Mapped to HTTP 404 by the API layer.</summary>
public class NotFoundException(string message) : Exception(message);
