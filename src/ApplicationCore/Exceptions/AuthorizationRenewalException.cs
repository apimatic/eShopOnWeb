using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The authorization went stale before fulfilment and PayPal would not renew it.
/// The message is phrased so an operator can act on it (e.g. ask the shopper to pay again).
/// </summary>
public class AuthorizationRenewalException : Exception
{
    public AuthorizationRenewalException(string message) : base(message)
    {
    }
}
