using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment provider (PayPal) rejects or fails an operation — a declined card,
/// an invalid request, or an upstream error. The <see cref="Exception.Message"/> is safe to
/// surface to callers and never contains card details.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        DebugId = debugId;
    }

    /// <summary>PayPal debug id for the failed call, when available — useful for support/correlation.</summary>
    public string? DebugId { get; }
}
