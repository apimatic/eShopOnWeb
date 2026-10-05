using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The billing system did not answer in time, or could not be reached.</summary>
    NoResponse,
    /// <summary>The billing system rejected the request as invalid (the caller can act on it).</summary>
    Rejected,
    /// <summary>The billing system answered with an error the caller cannot fix (5xx, auth, quota, unreadable body).</summary>
    Unavailable
}

/// <summary>
/// The single failure type raised by the billing gateway. Messages are caller-safe: they never carry
/// SDK type names, request URLs or credentials.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(
        BillingFailureKind kind,
        string message,
        int? providerStatusCode = null,
        IReadOnlyList<string>? errors = null,
        bool outcomeUnknown = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        Errors = errors ?? Array.Empty<string>();
        OutcomeUnknown = outcomeUnknown;
    }

    public BillingFailureKind Kind { get; }

    /// <summary>The HTTP status the billing system answered with, when it answered at all.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Validation messages returned by the billing system when it rejected the request.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when a write failed in a way that leaves open whether the billing system acted on it.</summary>
    public bool OutcomeUnknown { get; }

    public static BillingProviderException NoResponse(string message, bool outcomeUnknown = false, Exception? innerException = null) =>
        new(BillingFailureKind.NoResponse, message, outcomeUnknown: outcomeUnknown, innerException: innerException);
}
