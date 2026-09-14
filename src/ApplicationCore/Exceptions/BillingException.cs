using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// How a billing-provider interaction failed. Kept coarse on purpose: it is the single vocabulary the
/// API boundary maps to HTTP status codes, so the same kind of failure always looks the same to callers.
/// </summary>
public enum BillingFailureKind
{
    /// <summary>The caller asked for something the provider will never accept (unknown plan, missing detail).</summary>
    InvalidRequest,

    /// <summary>The requested plan, customer or subscription does not exist.</summary>
    NotFound,

    /// <summary>The request conflicts with provider state that a retry will not resolve.</summary>
    Conflict,

    /// <summary>Billing is not configured for this deployment (missing API key, subdomain, product family).</summary>
    NotConfigured,

    /// <summary>The provider rejected our credentials.</summary>
    Unauthorized,

    /// <summary>The provider is throttling us.</summary>
    RateLimited,

    /// <summary>The provider could not be reached, or timed out.</summary>
    ProviderUnavailable,

    /// <summary>The provider answered, but with a failure we cannot act on.</summary>
    ProviderError,

    /// <summary>We could not establish whether the operation took effect.</summary>
    IndeterminateOutcome
}

/// <summary>
/// The single failure type the billing integration raises. Provider exceptions, transport failures and
/// unreadable payloads are all translated to this at the integration boundary, so callers have one type
/// to handle and no provider or serializer detail ever reaches the wire.
/// </summary>
/// <remarks>
/// <see cref="Message"/> is always caller-safe — it is written by the integration, never copied from an
/// SDK or framework exception.
/// </remarks>
public class BillingException : Exception
{
    private static readonly IReadOnlyList<string> NoDetails = Array.Empty<string>();

    public BillingException(BillingFailureKind kind, string message)
        : this(kind, message, null, null)
    {
    }

    public BillingException(BillingFailureKind kind, string message, Exception? innerException)
        : this(kind, message, null, innerException)
    {
    }

    public BillingException(
        BillingFailureKind kind,
        string message,
        IReadOnlyList<string>? details,
        Exception? innerException)
        : base(message, innerException)
    {
        Kind = kind;
        Details = details ?? NoDetails;
    }

    public BillingFailureKind Kind { get; }

    /// <summary>Caller-safe validation messages the provider returned, when it returned any.</summary>
    public IReadOnlyList<string> Details { get; }
}
