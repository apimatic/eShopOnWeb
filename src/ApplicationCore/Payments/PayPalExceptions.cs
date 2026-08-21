using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A PayPal payment operation failed. Carries a caller-safe message and, where known, the HTTP status the
/// provider returned so the API boundary can map a provider 4xx back to a client 4xx and everything else to 5xx.
/// Never surfaces raw SDK/framework exception text.
/// </summary>
public class PayPalPaymentException : Exception
{
    public PayPalPaymentException(string message, int? providerStatusCode = null, Exception? inner = null,
        IReadOnlyList<string>? issues = null)
        : base(message, inner)
    {
        ProviderStatusCode = providerStatusCode;
        Issues = issues ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status PayPal returned, when a provider error carried one; null for transport/parse failures.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>PayPal "issue" codes from the error detail, when present.</summary>
    public IReadOnlyList<string> Issues { get; }
}

/// <summary>
/// The card payment returned a challenge that would require the shopper to approve in a browser (3DS /
/// payer-action-required). Per the task this is a STOP-and-report condition, not an approval round-trip.
/// </summary>
public class PayPalBuyerActionRequiredException : PayPalPaymentException
{
    public PayPalBuyerActionRequiredException(string message, string? actionRel, string? actionHref)
        : base(message, providerStatusCode: null)
    {
        ActionRel = actionRel;
        ActionHref = actionHref;
    }

    public string? ActionRel { get; }
    public string? ActionHref { get; }
}

/// <summary>
/// An authorization had gone stale and could no longer be renewed (reauthorized), so the fulfilment cannot
/// take the money. The message is phrased for an operator to act on.
/// </summary>
public class AuthorizationNotRenewableException : PayPalPaymentException
{
    public AuthorizationNotRenewableException(string message, int? providerStatusCode = null, Exception? inner = null,
        IReadOnlyList<string>? issues = null)
        : base(message, providerStatusCode, inner, issues)
    {
    }
}
