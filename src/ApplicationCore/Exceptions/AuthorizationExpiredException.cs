using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown by the gateway when a capture is attempted against an authorization that has gone stale
/// (its honeymoon period has passed). The payment service catches this to renew (reauthorize) the
/// hold before retrying the capture, rather than failing fulfilment outright.
/// </summary>
public class AuthorizationExpiredException : Exception
{
    public AuthorizationExpiredException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
