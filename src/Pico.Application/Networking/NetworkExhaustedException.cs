namespace Pico.Application.Networking;

/// <summary>
/// Raised when the VM IP allocation pool is exhausted. The pool
/// defaults to /24 (254 addresses) minus the gateway (.1) and
/// broadcast (.255) reservations = 252 usable slots. Operators
/// should bump the subnet (e.g. /16) or split into multiple networks
/// when this fires.
/// </summary>
public class NetworkExhaustedException : Exception
{
    public NetworkExhaustedException(string message) : base(message) { }
}