using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the billing integration presents to the rest of the application.
/// </summary>
/// <remarks>
/// <para>
/// Every failure inside the billing adapter — a provider error response, an unreachable provider, a response
/// body that could not be read — is converted to this type at the adapter boundary, so callers have one
/// failure type to handle instead of several unrelated ones.
/// </para>
/// <para>
/// <see cref="StatusCode"/> carries the status the caller should see, so that distinct failures stay
/// distinct: a provider rejection the caller can act on keeps its 4xx, while an unreachable provider or an
/// unreadable success body surfaces as 5xx. <see cref="Exception.Message"/> is always caller-safe — provider
/// and framework exception text is logged, never surfaced.
/// </para>
/// </remarks>
public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status code this failure should be reported to the caller as.</summary>
    public int StatusCode { get; }
}
