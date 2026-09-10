using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// The single failure type the subscription integration surfaces to its callers. It carries a
/// caller-facing HTTP status (already mapped from the provider's response — see
/// <see cref="MaxioSubscriptionService"/>) and a caller-safe message. Raw provider exception
/// detail is never propagated onto the wire.
/// </summary>
public sealed class MaxioApiException : Exception
{
    /// <summary>The HTTP status the PublicApi caller should receive.</summary>
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
