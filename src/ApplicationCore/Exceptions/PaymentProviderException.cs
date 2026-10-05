using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum PaymentProviderErrorKind
{
    /// <summary>The provider answered and refused the request (declined card, invalid state, validation).</summary>
    Rejected,
    /// <summary>The provider is unavailable or refused our own credentials; the caller did nothing wrong.</summary>
    Unavailable,
    /// <summary>The provider did not answer within the request's time budget.</summary>
    Timeout,
    /// <summary>A write was sent but its outcome could not be established.</summary>
    OutcomeUnknown,
    /// <summary>The provider asked for a shopper approval step (e.g. 3-D Secure) that this API does not support.</summary>
    PayerActionRequired
}

/// <summary>
/// A failure talking to the payment provider. Messages are caller-safe: they never carry card data or
/// raw SDK/HTTP diagnostics.
/// </summary>
public class PaymentProviderException : Exception
{
    public PaymentProviderException(
        PaymentProviderErrorKind kind,
        string message,
        int? providerStatusCode = null,
        string? providerErrorName = null,
        string? debugId = null,
        IReadOnlyList<string>? issues = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        ProviderErrorName = providerErrorName;
        DebugId = debugId;
        Issues = issues ?? Array.Empty<string>();
    }

    public PaymentProviderErrorKind Kind { get; }
    public int? ProviderStatusCode { get; }
    public string? ProviderErrorName { get; }
    /// <summary>PayPal's correlation id for the failed call (debug_id).</summary>
    public string? DebugId { get; }
    /// <summary>PayPal's issue codes (e.g. INSTRUMENT_DECLINED).</summary>
    public IReadOnlyList<string> Issues { get; }

    public bool IsUnknownOutcome => Kind is PaymentProviderErrorKind.OutcomeUnknown or PaymentProviderErrorKind.Timeout;
}
