using System;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// Raised when a call to the invoicing provider fails. <see cref="IsRefusal"/> distinguishes a
/// legitimate refusal that follows from the state the bill is in (for example, trying to change
/// a bill that has already been put to the shopper or withdrawn) from a genuine integration or
/// transport failure. Provider messages placed here are safe to surface to callers; secrets are
/// never included.
/// </summary>
public class InvoicingProviderException : Exception
{
    public InvoicingProviderException(string message, bool isRefusal = false, Exception? innerException = null)
        : base(message, innerException)
    {
        IsRefusal = isRefusal;
    }

    /// <summary>
    /// True when the provider refused the transition because of the bill's current state, rather
    /// than because of a fault in the integration.
    /// </summary>
    public bool IsRefusal { get; }
}
