using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the payment-provider boundary. Carries the provider's HTTP status (when the
/// provider answered) plus a caller-safe message; internal SDK detail never crosses this type.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? providerStatusCode = null, string? errorName = null,
        IReadOnlyList<string>? issues = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        ErrorName = errorName;
        Issues = issues ?? Array.Empty<string>();
    }

    /// <summary>The provider's HTTP status, or null for transport failures / unreadable responses.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>PayPal's error name (e.g. UNPROCESSABLE_ENTITY), when known.</summary>
    public string? ErrorName { get; }

    /// <summary>PayPal's issue descriptions, safe to show an operator.</summary>
    public IReadOnlyList<string> Issues { get; }

    /// <summary>True when the provider answered with a 4xx — the caller can act on it.</summary>
    public bool IsClientError => ProviderStatusCode is >= 400 and < 500;
}
