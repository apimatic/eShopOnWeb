using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at fulfilment when a stale authorization can no longer be renewed. The message is phrased so
/// an operator can act on it (the shopper must pay the order again).
/// </summary>
public class ReauthorizationFailedException : Exception
{
    public ReauthorizationFailedException(string message) : base(message) { }
}
