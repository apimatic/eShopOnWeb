using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The provider refused the request itself (validation, conflict); the caller can act on it.</summary>
    Rejected,

    /// <summary>Our credentials, site or catalog configuration are wrong; the caller cannot fix it.</summary>
    Misconfigured,

    /// <summary>The provider is unreachable, timed out, throttled us, failed, or answered unreadably.</summary>
    Unavailable
}

/// <summary>
/// A definite failure talking to the billing provider. The message is safe to show to callers:
/// it never carries provider URLs, credentials or SDK type names.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(BillingFailureKind kind, string message, int? providerStatusCode = null,
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
