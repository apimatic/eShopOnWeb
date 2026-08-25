using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Wraps a failed PayPal API call with an operator-actionable message. Carries PayPal's own
/// error name/debug id so an operator (or support ticket) can trace it back to PayPal's side.</summary>
public class PayPalOperationException : Exception
{
    public PayPalOperationException(string message, string? payPalErrorName = null, string? payPalDebugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        PayPalErrorName = payPalErrorName;
        PayPalDebugId = payPalDebugId;
    }

    public string? PayPalErrorName { get; }
    public string? PayPalDebugId { get; }
}
