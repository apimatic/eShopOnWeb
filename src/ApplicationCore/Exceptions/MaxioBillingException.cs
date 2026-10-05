using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the subscription billing provider (Maxio Advanced Billing) rejects a call,
/// is unreachable, or returns an unusable response. Carries the provider HTTP status when
/// one was received, and a caller-safe message that never echoes provider transport detail.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The provider's HTTP status code, when the provider answered; null for transport failures.
    /// </summary>
    public int? StatusCode { get; }
}