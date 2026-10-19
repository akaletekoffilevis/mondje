namespace Mondje.Application.Common.Exceptions;

/// <summary>The request conflicts with the current state of the resource
/// (e.g. creating a transaction whose UUID already exists). Mapped to HTTP 409.</summary>
public class ConflictException(string message) : Exception(message);
