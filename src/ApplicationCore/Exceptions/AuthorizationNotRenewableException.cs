using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The authorization went stale before fulfilment and the processor can no longer renew it.
/// The message is operator-actionable (the shopper must pay again).
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message)
    {
    }
}
