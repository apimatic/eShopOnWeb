using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown at fulfilment when an authorization has gone stale and can no longer be renewed
/// (reauthorized). The message is phrased so an operator knows the action to take: the hold has
/// expired and the shopper must re-pay the order before it can be fulfilled.
/// </summary>
public class ReauthorizationNotPossibleException : Exception
{
    public ReauthorizationNotPossibleException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
