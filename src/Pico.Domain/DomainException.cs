namespace Pico.Domain;

/// <summary>
/// Thrown when domain logic rules are violated (e.g. invalid state transition).
/// Distinct from ArgumentException so callers can pattern-match on domain errors specifically.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception inner) : base(message, inner) { }
}