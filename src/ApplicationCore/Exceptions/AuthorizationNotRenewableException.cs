using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

// The payment hold has gone stale and PayPal will not renew it (e.g. past the reauthorization
// window). An operator must be told plainly that fulfilment cannot proceed against this authorization.
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message)
    {
    }
}
