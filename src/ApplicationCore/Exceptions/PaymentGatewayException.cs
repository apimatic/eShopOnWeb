using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the PayPal gateway surfaces to the rest of the app. It carries a
/// caller-safe message (never the raw SDK/provider text) and, when the failure came from PayPal
/// with an HTTP status, that status — so the API boundary can map a provider 4xx (the caller can
/// act on it) to a client 4xx and everything else to a 5xx.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>The HTTP status PayPal returned, when the failure was an API error. Null for transport/parse failures.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>True when PayPal rejected the request with a 4xx the caller could act on.</summary>
    public bool IsClientError => ProviderStatusCode is >= 400 and < 500;
}
