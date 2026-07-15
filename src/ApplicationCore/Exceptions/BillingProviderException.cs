using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejects a request or is unreachable. Carries a friendly message only —
/// never the raw provider exception text — so it is safe to surface to the storefront (plan.md §2.5:
/// Maxio failures must never crash or roll back eShopOnWeb's own state).
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
