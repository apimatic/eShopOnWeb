using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Why a billing operation failed, in terms the API layer can map to a status code without
/// knowing which billing provider produced it.
/// </summary>
public enum BillingErrorKind
{
    /// <summary>The integration is misconfigured or its credentials were rejected. Not the caller's fault.</summary>
    Configuration,

    /// <summary>The caller asked for a plan or resource that does not exist in the configured catalog.</summary>
    NotFound,

    /// <summary>The billing system rejected the request content.</summary>
    Validation,

    /// <summary>The billing system is unreachable, throttling, or failing. Retrying later may succeed.</summary>
    Unavailable,

    /// <summary>The billing system returned something the integration could not interpret.</summary>
    Unexpected
}

/// <summary>
/// Provider-neutral failure raised by <see cref="Interfaces.ISubscriptionBillingService"/> implementations.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        BillingErrorKind kind,
        string message,
        IEnumerable<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Errors = errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray() ?? Array.Empty<string>();
    }

    public BillingErrorKind Kind { get; }

    /// <summary>Field-level messages reported by the billing system, safe to echo back to the caller.</summary>
    public IReadOnlyList<string> Errors { get; }
}
