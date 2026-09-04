using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal declines the card/authorization (mapped to HTTP 402).
/// Carries only the PayPal issue code and description - never card data.
/// </summary>
public class PaymentDeclinedException : Exception
{
    public string Issue { get; }
    public string? DebugId { get; }

    public PaymentDeclinedException(string issue, string message, string? debugId = null) : base(message)
    {
        Issue = issue;
        DebugId = debugId;
    }
}
