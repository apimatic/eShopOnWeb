namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised by the PayPal client when a capture is rejected because the authorization has gone stale
/// (past its honor period) but may still be renewable. Orchestration reacts by re-authorizing and
/// retrying the capture; if the renewal itself fails, an <see cref="AuthorizationNotRenewableException"/>
/// is raised instead.
/// </summary>
public class StaleAuthorizationException : PaymentException
{
    public StaleAuthorizationException(string message) : base(message) { }
}
