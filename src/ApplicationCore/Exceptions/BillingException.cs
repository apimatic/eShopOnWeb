using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// How a billing operation failed. The boundary maps this — and only this — onto an HTTP status, so that a
/// caller can tell "you asked for something invalid" apart from "the provider is unreachable".
/// </summary>
public enum BillingFailureKind
{
    /// <summary>The provider rejected the request as invalid (validation).</summary>
    InvalidRequest,

    /// <summary>The thing being addressed does not exist (for example an unknown plan handle).</summary>
    NotFound,

    /// <summary>The request conflicts with existing provider state.</summary>
    Conflict,

    /// <summary>The caller is not permitted to perform the operation, or credentials were rejected.</summary>
    NotPermitted,

    /// <summary>The provider could not be reached, timed out, or failed internally.</summary>
    Unavailable,

    /// <summary>The provider answered, but the answer could not be understood.</summary>
    Unreadable,

    /// <summary>Billing is not configured (or is misconfigured) in this deployment.</summary>
    NotConfigured,

    /// <summary>A write may or may not have taken effect and the outcome could not be established.</summary>
    OutcomeUnknown
}

/// <summary>
/// The single failure type the rest of eShopOnWeb sees for billing. Everything the billing provider or the
/// transport can throw is translated into this at the integration boundary, so no SDK type, provider type
/// name, or raw provider message ever escapes into application code or onto the wire.
/// </summary>
public class BillingException : Exception
{
    public BillingException(
        BillingFailureKind kind,
        string message,
        IReadOnlyList<string>? details = null,
        int? providerStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        Details = Scrub(details);
    }

    public BillingFailureKind Kind { get; }

    /// <summary>The HTTP status the provider returned, when there was one.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>
    /// Caller-safe validation messages, when the provider supplied any. Capped in count and length so an
    /// unexpectedly large or chatty provider payload cannot be reflected wholesale to the caller.
    /// </summary>
    public IReadOnlyList<string> Details { get; }

    private const int MaxDetails = 10;
    private const int MaxDetailLength = 300;

    private static IReadOnlyList<string> Scrub(IReadOnlyList<string>? details)
    {
        if (details is null || details.Count == 0) return Array.Empty<string>();

        return details
            .Where(detail => !string.IsNullOrWhiteSpace(detail))
            .Select(detail => detail.Trim())
            .Select(detail => detail.Length <= MaxDetailLength ? detail : detail.Substring(0, MaxDetailLength) + "…")
            .Take(MaxDetails)
            .ToArray();
    }
}
