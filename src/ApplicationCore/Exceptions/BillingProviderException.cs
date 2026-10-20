using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The provider could not be reached, timed out, throttled us, or rejected our credentials.</summary>
    Unavailable = 1,

    /// <summary>The provider refused the request as invalid; <see cref="BillingProviderException.Errors"/> says why.</summary>
    Rejected = 2,

    /// <summary>The provider failed, or answered with something we could not process.</summary>
    ProviderError = 3
}

/// <summary>
/// A billing-provider call failed. <see cref="Exception.Message"/> is safe to show to API callers;
/// provider and transport details stay on <see cref="Exception.InnerException"/>.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, BillingFailureKind kind, int? providerStatusCode = null,
        IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }

    /// <summary>The provider's HTTP status, when it answered at all.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>The provider's validation messages, for <see cref="BillingFailureKind.Rejected"/>.</summary>
    public IReadOnlyList<string> Errors { get; }
}
