using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the PayPal gateway surfaces for every provider-side problem — an API error, a
/// transport failure, or an unreadable response. Carries a caller-safe message plus, where known, the
/// provider HTTP status and PayPal's own <c>debug_id</c> for correlation. Never carries card data.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message, int? providerStatusCode = null, string? debugId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        DebugId = debugId;
    }

    /// <summary>The provider HTTP status, where the error path exposed one; otherwise null.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>PayPal's <c>debug_id</c> correlation token, where present.</summary>
    public string? DebugId { get; }

    /// <summary>PayPal's top-level error name (e.g. UNPROCESSABLE_ENTITY), where the typed body carried one.</summary>
    public string? ProviderName { get; init; }

    /// <summary>PayPal's first issue code (e.g. AUTHORIZATION_EXPIRED), where present — lets callers branch.</summary>
    public string? ProviderIssue { get; init; }

    /// <summary>
    /// True when the transport failed after the request may already have been received, so the outcome is
    /// unknown and the caller must reconcile by re-reading rather than treat this as a definite failure.
    /// </summary>
    public bool OutcomeUnknown { get; init; }
}
