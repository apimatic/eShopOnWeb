using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The billing system rejected the request on its merits; the caller can act on the errors.</summary>
    Rejected,

    /// <summary>The billing system failed, refused our credentials, or answered with something unreadable.</summary>
    Unavailable,

    /// <summary>The billing system did not answer within the time budget.</summary>
    Timeout
}

/// <summary>
/// A failure reported by (or reaching) the billing system. <see cref="Exception.Message"/> and
/// <see cref="Errors"/> are safe to return to the caller; the provider exception is kept as the inner exception.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(BillingFailureKind kind, string message, int? providerStatusCode = null,
        IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }
    public int? ProviderStatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
}
