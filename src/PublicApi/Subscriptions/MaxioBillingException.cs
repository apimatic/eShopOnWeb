using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// The single failure type that escapes the Maxio billing integration boundary.
/// Carries an HTTP status the endpoints map back to the caller plus a caller-safe message.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingFailureKind Kind { get; }

    /// <summary>The provider (Maxio) HTTP status when known; otherwise null.</summary>
    public HttpStatusCode? ProviderStatusCode { get; }

    /// <summary>Provider validation messages, when the provider surfaced structured ones.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }

    /// <summary>The status the endpoints should return to our own callers.</summary>
    public int ResponseStatusCode => Kind switch
    {
        MaxioBillingFailureKind.NotFound => (int)HttpStatusCode.NotFound,
        MaxioBillingFailureKind.Conflict => (int)HttpStatusCode.Conflict,
        MaxioBillingFailureKind.Rejected =>
            ProviderStatusCode is { } s && (int)s >= 400 && (int)s <= 499
                ? (int)s
                : (int)HttpStatusCode.BadRequest,
        MaxioBillingFailureKind.ProviderUnavailable => (int)HttpStatusCode.BadGateway,
        MaxioBillingFailureKind.UnparseableProviderResponse => (int)HttpStatusCode.BadGateway,
        MaxioBillingFailureKind.Timeout => (int)HttpStatusCode.GatewayTimeout,
        _ => (int)HttpStatusCode.InternalServerError,
    };

    public MaxioBillingException(
        MaxioBillingFailureKind kind,
        string callerSafeMessage,
        Exception? inner = null,
        HttpStatusCode? providerStatusCode = null,
        IReadOnlyList<string>? providerErrors = null)
        : base(callerSafeMessage, inner)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }
}

public enum MaxioBillingFailureKind
{
    /// <summary>The provider rejected the request (4xx other than not-found).</summary>
    Rejected,

    /// <summary>A looked-up provider resource does not exist (provider 404).</summary>
    NotFound,

    /// <summary>The request is well-formed but cannot proceed (e.g. plan requires a payment method).</summary>
    Conflict,

    /// <summary>Transport failure, provider 5xx, or unreadable provider response.</summary>
    ProviderUnavailable,

    /// <summary>The provider returned a 2xx whose body could not be deserialized.</summary>
    UnparseableProviderResponse,

    /// <summary>The call exceeded its time budget.</summary>
    Timeout,

    /// <summary>Our own configuration/credentials are wrong (never a caller's error).</summary>
    Configuration,
}
