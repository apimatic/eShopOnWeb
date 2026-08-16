namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A capture was attempted against an authorization that has gone stale (its honor period has
/// passed). The caller should renew the authorization (reauthorize) and try the capture again.
/// </summary>
public class AuthorizationExpiredException : PaymentException
{
    public AuthorizationExpiredException(string message) : base(message)
    {
    }
}
