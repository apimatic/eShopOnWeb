using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejects a request or cannot be reached. A Maxio failure must never
/// roll back or block eShopOnWeb's own order lifecycle — callers surface this as a friendly error instead
/// of letting it escape into unrelated flows.
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
