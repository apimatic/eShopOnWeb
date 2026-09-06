using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// How a billing operation failed. The kind — not the provider's raw status — is what the API surface
/// maps onto an HTTP response, so that distinct failures stay distinct for the caller.
/// </summary>
public enum BillingFailureKind
{
    /// <summary>The integration is mis-configured (missing API key, unknown product family, ...). Never the caller's fault.</summary>
    Configuration,

    /// <summary>The caller asked for something the billing system cannot accept as framed.</summary>
    InvalidRequest,

    /// <summary>The requested plan, customer or subscription does not exist.</summary>
    NotFound,

    /// <summary>The billing system deterministically rejected the request (validation). Retrying verbatim cannot succeed.</summary>
    Rejected,

    /// <summary>The billing system could not be reached, or did not answer in time. Retrying later may succeed.</summary>
    Unavailable,

    /// <summary>The request may or may not have taken effect; provider state must be re-read to settle it.</summary>
    OutcomeUnknown,

    /// <summary>Anything else the billing system reported.</summary>
    Unknown
}

/// <summary>
/// The single failure type the billing integration raises. Provider exceptions — API errors, transport
/// failures and unreadable payloads alike — are translated to this at the integration boundary so that
/// callers have one type to handle and no provider detail leaks onto the wire.
/// </summary>
public class BillingException : Exception
{
    public BillingException(
        BillingFailureKind kind,
        string message,
        int? providerStatusCode = null,
        IReadOnlyList<string>? providerMessages = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        ProviderMessages = providerMessages ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }

    /// <summary>The HTTP status the billing provider returned, when one could be established.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Caller-safe validation messages the billing provider returned, when it returned any.</summary>
    public IReadOnlyList<string> ProviderMessages { get; }
}
