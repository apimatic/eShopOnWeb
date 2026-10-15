using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The provider refused the request on its merits (validation); the reasons are safe to show the caller.</summary>
    Rejected,

    /// <summary>The provider could not be reached, timed out, or failed on its side; retrying later may succeed.</summary>
    Unavailable,

    /// <summary>The provider refused our credentials or configuration (401/403/unknown product family); the caller cannot fix it.</summary>
    Misconfigured
}

/// <summary>
/// A billing-provider failure translated at the integration boundary. <see cref="Exception.Message"/> is caller-safe:
/// it never carries provider URLs, SDK type names or request bodies.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(BillingFailureKind kind, string message, int? providerStatusCode = null,
        IReadOnlyList<string>? reasons = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        Reasons = reasons ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }

    public int? ProviderStatusCode { get; }

    public IReadOnlyList<string> Reasons { get; }
}
