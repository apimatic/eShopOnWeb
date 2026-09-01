using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The PayPal authorization holding the shopper's funds went stale and can no longer be
/// renewed (PayPal allows reauthorization only within a limited window). An operator must
/// ask the shopper to pay again; the order has been moved back to awaiting payment.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message)
    {
    }
}
