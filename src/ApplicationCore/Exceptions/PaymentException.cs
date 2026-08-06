using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment could not be completed for a reason attributable to the request - a declined
/// card, an invalid saved-card reference, or a PayPal business-rule rejection. Surfaced to
/// the caller as a 4xx. The message is safe to return; it never contains card details.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }

    public PaymentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
