using Pico.Domain;

namespace Pico.Application.Common;

/// <summary>
/// Result wrapper for operations that can fail without throwing.
/// Used by Application Services for provisioning, billing, etc.
/// </summary>
public class Result<T>
{
    public T? Value { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public DomainException? Exception { get; init; }

    public static Result<T> Success(T value) => new() { Value = value, IsSuccess = true };
    public static Result<T> Failure(string error) => new() { ErrorMessage = error, IsSuccess = false };
    public static Result<T> Failure(DomainException ex) => new()
    {
        ErrorMessage = ex.Message,
        Exception = ex,
        IsSuccess = false
    };
}