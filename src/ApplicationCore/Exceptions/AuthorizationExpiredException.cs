using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

// A capture attempt failed because PayPal reports the authorization is no longer active. The
// fulfilment orchestration catches this to trigger a reauthorization attempt before giving up.
public class AuthorizationExpiredException : Exception
{
    public AuthorizationExpiredException(string message) : base(message)
    {
    }
}
