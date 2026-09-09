using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio Advanced Billing provider fails or rejects an operation.
/// Carries the provider's HTTP status code when one was received, so callers can
/// distinguish "rejected by the provider" (4xx) from "provider unreachable or
/// unreadable" (status unknown). The message is always caller-safe.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The HTTP status returned by the provider, or null when no usable status
    /// was captured (transport failure or unparseable response).
    /// </summary>
    public int? StatusCode { get; }
}
