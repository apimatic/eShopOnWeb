using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The provider refused the request itself (validation and similar) — the shopper can act on it.</summary>
    Rejected,

    /// <summary>The provider could not be reached, timed out, failed (5xx/429) or answered unreadably.</summary>
    Unavailable,

    /// <summary>Our credentials, site or catalog configuration were refused (401/403, unknown product family).</summary>
    Misconfigured
}

/// <summary>
/// A billing-provider failure translated at the integration boundary. <see cref="Exception.Message"/> and
/// <see cref="ProviderErrors"/> are safe to return to API callers; provider internals stay in <see cref="Exception.InnerException"/>.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, BillingFailureKind kind, int? providerStatusCode = null,
        IReadOnlyList<string>? providerErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }
    public int? ProviderStatusCode { get; }
    public IReadOnlyList<string> ProviderErrors { get; }
}
