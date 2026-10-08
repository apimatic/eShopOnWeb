using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum PaymentGatewayFailure
{
    /// <summary>The provider refused the request itself (invalid data); nothing was processed. The caller can fix it.</summary>
    Rejected,
    /// <summary>The provider could not be used (credentials, throttling); nothing was processed.</summary>
    Unavailable,
    /// <summary>The provider may or may not have acted (no answer, unreadable answer, provider error).</summary>
    OutcomeUnknown
}

/// <summary>
/// A payment provider failure. <see cref="Exception.Message"/> is always safe to show to the caller.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(PaymentGatewayFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public PaymentGatewayFailure Failure { get; }

    /// <summary>The provider did not answer within the time budget.</summary>
    public bool TimedOut { get; init; }

    /// <summary>HTTP status the provider answered with, when it answered.</summary>
    public int? ProviderStatusCode { get; init; }

    /// <summary>The provider's own error code, when its error body carried one.</summary>
    public string? ProviderErrorCode { get; init; }

    /// <summary>The provider's reference for the failed request (correlation id), when it sent one.</summary>
    public string? ProviderReference { get; init; }
}
