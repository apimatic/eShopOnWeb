using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the payment provider failed: transport failure, an unreadable response, or a
/// provider error that the caller cannot act on directly. Maps to HTTP 502. The message is
/// caller-safe; provider detail that is safe to show (PayPal error name/message/debug id)
/// is carried on the properties.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? providerStatusCode = null, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        DebugId = debugId;
    }

    public int? ProviderStatusCode { get; }
    public string? DebugId { get; }
}
