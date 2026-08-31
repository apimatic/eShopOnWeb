using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the payment provider failed. Carries the provider's HTTP status (when one was received)
/// and debug id so callers can act; the message is always caller-safe.
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
