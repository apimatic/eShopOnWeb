using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order's payment authorization has gone stale (past its honor period) and the
/// payment provider refuses to renew it — e.g. it was already voided/captured, or it is beyond the
/// provider's reauthorization window. Distinct from a transient failure: an operator must decide
/// what to do next (typically re-collect payment from the shopper), retrying will not help.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message)
    {
    }

    public AuthorizationNotRenewableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
