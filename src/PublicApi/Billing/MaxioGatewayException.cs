using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public enum MaxioFailureKind
{
    /// <summary>Maxio refused the request (4xx other than auth/404/429). It did not act.</summary>
    Rejected,

    /// <summary>The resource looked up does not exist.</summary>
    NotFound,

    /// <summary>Our credentials or site configuration were refused (401/403), or no credential could be applied.</summary>
    Configuration,

    /// <summary>Our quota is spent (429).</summary>
    RateLimited,

    /// <summary>Maxio answered 5xx (or another non-success status) - it may or may not have acted.</summary>
    ProviderError,

    /// <summary>No usable response: connection failure or timeout.</summary>
    Unreachable,

    /// <summary>Maxio answered but the body did not match the SDK model.</summary>
    Unreadable
}

/// <summary>
/// The single failure type the Maxio gateway raises. Carries a caller-safe message only; the SDK exception
/// (with URL and type names) stays in <see cref="Exception.InnerException"/> for logs.
/// </summary>
public sealed class MaxioGatewayException : Exception
{
    public MaxioGatewayException(
        string operation,
        MaxioFailureKind kind,
        string message,
        HttpStatusCode? providerStatus = null,
        IReadOnlyList<string>? providerErrors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        Kind = kind;
        ProviderStatus = providerStatus;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    public string Operation { get; }

    public MaxioFailureKind Kind { get; }

    public HttpStatusCode? ProviderStatus { get; }

    /// <summary>Maxio's own <c>errors[]</c> messages, when it sent any.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }

    /// <summary>
    /// For a write: true when Maxio may have acted although the call failed - no response, a 5xx, or a success
    /// body we could not read. Such an outcome must be settled by re-reading Maxio, never reported as a failure.
    /// </summary>
    public bool OutcomeUnknown =>
        Kind is MaxioFailureKind.Unreachable or MaxioFailureKind.ProviderError ||
        (Kind == MaxioFailureKind.Unreadable && ProviderStatus is { } status && (int)status is >= 200 and < 300);
}
