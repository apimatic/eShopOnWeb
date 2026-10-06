using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public enum SquareFailureKind
{
    /// <summary>No merchant is connected and no fallback access token is configured, or the connection was revoked.</summary>
    NotConnected,
    /// <summary>Square refused our credentials (401/403).</summary>
    AuthorizationFailed,
    /// <summary>Square rejected the request (4xx other than auth/rate limit).</summary>
    Rejected,
    /// <summary>Square throttled us (429) or is failing (5xx), or could not be reached — nothing was written.</summary>
    Unavailable,
    /// <summary>A write may or may not have reached Square and could not be settled.</summary>
    OutcomeUnknown,
    /// <summary>Square's answer could not be interpreted.</summary>
    UnreadableResponse,
}

/// <summary>
/// The single failure type the Square integration raises. Carries a caller-safe message,
/// the provider status (when Square answered) and Square's error codes.
/// </summary>
public sealed class SquareIntegrationException : Exception
{
    public SquareIntegrationException(
        SquareFailureKind kind,
        string message,
        HttpStatusCode? providerStatus = null,
        IReadOnlyList<string>? errorCodes = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatus = providerStatus;
        ErrorCodes = errorCodes ?? Array.Empty<string>();
    }

    public SquareFailureKind Kind { get; }
    public HttpStatusCode? ProviderStatus { get; }
    public IReadOnlyList<string> ErrorCodes { get; }

    /// <summary>
    /// True when a write may have landed at Square: a transport failure or timeout, a 5xx,
    /// or a 2xx whose body could not be read. Such writes are settled, never reported as failed.
    /// </summary>
    public bool MayHaveReachedSquare => Kind is SquareFailureKind.OutcomeUnknown or SquareFailureKind.UnreadableResponse
        || (Kind == SquareFailureKind.Unavailable && (ProviderStatus is null || (int)ProviderStatus >= 500));

    public bool IsNotFound => Kind == SquareFailureKind.Rejected && ProviderStatus == HttpStatusCode.NotFound;
}

/// <summary>Another request already holds the claim for this operator action.</summary>
public sealed class SquareOperationInProgressException : Exception
{
    public SquareOperationInProgressException(string message) : base(message) { }
}

/// <summary>The request is invalid for the integration (caller error), e.g. an item not yet synced.</summary>
public sealed class SquareRequestException : Exception
{
    public SquareRequestException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
