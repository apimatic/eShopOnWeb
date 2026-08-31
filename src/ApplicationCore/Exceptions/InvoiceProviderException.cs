using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment provider rejects or fails a request for reasons that are not a legitimate
/// state refusal (which is modelled by <see cref="InvoiceStateException"/>). Surfaced as a bad-gateway
/// so the caller can tell an upstream provider failure apart from a client mistake. The message is
/// crafted to never carry credentials.
/// </summary>
public class InvoiceProviderException : Exception
{
    public InvoiceProviderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
