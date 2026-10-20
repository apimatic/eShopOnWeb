using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The provider refused the request because of what was asked; the caller can act on it.</summary>
    Rejected,

    /// <summary>The provider could not be reached, failed, or returned something unreadable.</summary>
    Unavailable,

    /// <summary>The provider did not answer within the time budget.</summary>
    Timeout,

    /// <summary>Our own credentials or quota were refused; the caller did nothing wrong.</summary>
    Misconfigured
}

/// <summary>
/// A billing-provider failure translated at the integration boundary. <see cref="Exception.Message"/>
/// is always safe to show to a caller.
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
