using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The single failure type the subscription-billing boundary raises. It converts every SDK/transport
/// failure into one type carrying a caller-safe <see cref="Message"/> and, where the provider supplied
/// one, the upstream HTTP <see cref="StatusCode"/> used to map to a caller-facing response. A null
/// <see cref="StatusCode"/> means no response was received (transport failure/timeout/unreadable body).
/// </summary>
public sealed class SubscriptionBillingException : Exception
{
    /// <summary>The provider HTTP status, when one was received; otherwise null.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// True when the caller's input was rejected (a provider 4xx that is not an auth/rate-limit failure),
    /// so the caller can act on it — as opposed to a provider-side/transport fault they cannot fix.
    /// </summary>
    public bool IsCallerError { get; }

    public SubscriptionBillingException(string message, int? statusCode = null, bool isCallerError = false, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        IsCallerError = isCallerError;
    }
}
