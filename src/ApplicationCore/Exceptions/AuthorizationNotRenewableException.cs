using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The authorization went stale before fulfilment and PayPal can no longer renew it.
/// The message is written for an operator, telling them how to recover.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message)
    {
    }
}
