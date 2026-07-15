using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

// Raised by IBillingClient whenever the billing provider call fails (a rejected request,
// or a connection-level failure). Never rolls back or blocks eShopOnWeb's own order lifecycle —
// callers surface this as a friendly error on the subscription flow only.
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
