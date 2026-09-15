using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal operation fails. Carries a caller-/operator-safe message plus the provider's
/// HTTP status so the API boundary can map a shopper-actionable rejection (4xx) distinctly from a
/// provider outage (mapped to 502). It never carries card details or raw SDK/framework text.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? providerStatusCode = null, string? issue = null, Exception? inner = null)
        : base(message, inner)
    {
        ProviderStatusCode = providerStatusCode;
        Issue = issue;
    }

    /// <summary>The HTTP status PayPal returned, when known.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>PayPal's machine-readable issue code, when one was present (e.g. AUTHORIZATION_EXPIRED).</summary>
    public string? Issue { get; }

    /// <summary>A 4xx from PayPal is something the caller/operator can act on; everything else is treated as an outage.</summary>
    public bool IsClientError => ProviderStatusCode is >= 400 and < 500;

    /// <summary>
    /// True when the failure indicates the authorization's honor period has lapsed and it could not be
    /// captured as-is — the operator flow should try to renew (re-authorize) it. PayPal signals this with
    /// an issue code containing "EXPIRED" (exact code is live-traffic-only; matched defensively).
    /// </summary>
    public bool IndicatesAuthorizationExpired =>
        Issue is not null && Issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that needs the shopper to approve in a
/// browser (3-D Secure / payer action). Per the integration's mandate we STOP and report this rather
/// than building an approval round-trip.
/// </summary>
public class BuyerActionRequiredException : PaymentGatewayException
{
    public BuyerActionRequiredException(string message)
        : base(message, providerStatusCode: 402)
    {
    }
}
