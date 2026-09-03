using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The kind of billing failure, chosen so the API layer can map it to a caller-facing HTTP status without
/// needing to know anything about the Maxio SDK. A provider failure that is <em>our</em> fault
/// (bad credentials, spent quota) or a transport fault is <see cref="ProviderUnavailable"/> — never
/// surfaced to the caller as if they did something wrong.
/// </summary>
public enum MaxioBillingFailureKind
{
    /// <summary>The caller's request was rejected by the provider (validation / 422 / 400).</summary>
    InvalidRequest,

    /// <summary>A referenced entity (e.g. the plan handle) does not exist.</summary>
    NotFound,

    /// <summary>The request conflicts with existing state.</summary>
    Conflict,

    /// <summary>Provider unreachable, timed out, throttled, or rejected our own credentials — not the caller's fault.</summary>
    ProviderUnavailable,

    /// <summary>An unexpected/unclassified failure.</summary>
    Unexpected
}

/// <summary>
/// The single failure type raised by <see cref="Subscriptions.ISubscriptionBillingService"/>. Its
/// <see cref="Exception.Message"/> is always caller-safe (it never carries SDK/JSON internals); provider
/// detail is logged at the boundary that constructs it, not echoed to the caller.
/// </summary>
public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, MaxioBillingFailureKind kind, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public MaxioBillingFailureKind Kind { get; }
}
