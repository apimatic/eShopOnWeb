using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown at fulfilment when the authorization has gone stale and can no longer be renewed
/// (reauthorization is no longer possible). The message is phrased for an operator to act on:
/// the funds hold has lapsed and the shopper must be asked to pay again.
/// </summary>
public class AuthorizationRenewalException : Exception
{
    public AuthorizationRenewalException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
