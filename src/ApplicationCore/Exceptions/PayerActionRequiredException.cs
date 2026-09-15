using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal required a shopper challenge (for example 3-D Secure) that cannot be completed without a browser.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message, string? payPalOrderId, string? debugId)
        : base(message)
    {
        PayPalOrderId = payPalOrderId;
        DebugId = debugId;
    }

    public string? PayPalOrderId { get; }
    public string? DebugId { get; }
}
