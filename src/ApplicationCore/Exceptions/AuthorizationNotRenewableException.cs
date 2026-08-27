using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The authorization hold went stale before fulfilment and PayPal can no longer renew it.
/// The message is phrased so an operator knows what to do next.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message)
    {
    }
}
