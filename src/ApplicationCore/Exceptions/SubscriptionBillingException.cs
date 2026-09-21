using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Single failure type the subscription-billing boundary raises for every provider failure —
/// API errors, transport failures, and unreadable responses alike — so callers reason about one
/// type instead of the SDK's several. <see cref="StatusCode"/> carries the provider HTTP status
/// where one is available (null for transport/unknown failures), and drives the caller-facing
/// status mapping.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>Provider HTTP status code, when known; otherwise null (transport/unknown).</summary>
    public int? StatusCode { get; }
}
