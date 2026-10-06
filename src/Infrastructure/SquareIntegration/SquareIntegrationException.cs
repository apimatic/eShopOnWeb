using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public enum SquareFailureKind
{
    /// <summary>No merchant is connected and no access token is configured.</summary>
    NotConnected,
    /// <summary>Square refused our credentials (401/403): the merchant must reconnect.</summary>
    AuthorizationFailed,
    /// <summary>Square rate-limited us.</summary>
    RateLimited,
    /// <summary>Square rejected the request (4xx other than auth/rate limit).</summary>
    Rejected,
    /// <summary>Square answered with a 5xx, an unreadable body, or could not be reached; nothing was written.</summary>
    Unavailable,
    /// <summary>A write may or may not have reached Square; it is recorded and will be settled later.</summary>
    OutcomeUnknown,
    /// <summary>The eShop side is in a state that prevents the operation (e.g. a concurrent write holds the claim).</summary>
    Conflict,
}

/// <summary>
/// The one failure type the Square integration lets out. Its message is safe to show to API callers:
/// it never carries tokens, request bodies or SDK internals.
/// </summary>
public sealed class SquareIntegrationException : Exception
{
    public SquareIntegrationException(SquareFailureKind kind, string message,
        HttpStatusCode? squareStatus = null, string? squareErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        SquareStatus = squareStatus;
        SquareErrorCode = squareErrorCode;
    }

    public SquareFailureKind Kind { get; }

    /// <summary>The HTTP status Square answered with, when it answered.</summary>
    public HttpStatusCode? SquareStatus { get; }

    /// <summary>The first <c>errors[].code</c> of Square's error body, when it carried one.</summary>
    public string? SquareErrorCode { get; }
}
