using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum BillingErrorKind
{
    /// <summary>The request named something the shop does not offer (e.g. an unknown plan handle).</summary>
    InvalidRequest,

    /// <summary>The billing provider rejected the request on its merits (validation).</summary>
    Rejected,

    /// <summary>An identical request is already being processed.</summary>
    InProgress,

    /// <summary>The billing provider is temporarily unavailable (timeout, 5xx, rate limit).</summary>
    Unavailable,

    /// <summary>The billing integration is misconfigured, or the provider answered unexpectedly.</summary>
    ProviderFailure
}

/// <summary>
/// A billing failure carrying only caller-safe text. Provider and SDK details stay in the logs.
/// </summary>
public sealed class BillingException : Exception
{
    public BillingException(BillingErrorKind kind, string message, IReadOnlyList<string>? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Details = details ?? Array.Empty<string>();
    }

    public BillingErrorKind Kind { get; }

    /// <summary>Provider validation messages that are safe to show the caller.</summary>
    public IReadOnlyList<string> Details { get; }
}
