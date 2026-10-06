using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The provider did not answer within the time budget.</summary>
    Timeout,
    /// <summary>The provider could not be reached (connection failure).</summary>
    Unreachable,
    /// <summary>The provider rejected the request for a reason the caller can act on (e.g. validation).</summary>
    Rejected,
    /// <summary>Our own configuration or credentials are wrong (401/403, unknown product family, ...).</summary>
    Misconfigured,
    /// <summary>The provider failed (5xx) or answered with a body we could not read.</summary>
    ProviderError
}

/// <summary>A failure talking to the billing provider, already translated out of the provider SDK.</summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(BillingFailureKind kind, string message, int? providerStatusCode = null,
        IReadOnlyList<string>? providerErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }
    public int? ProviderStatusCode { get; }
    public IReadOnlyList<string> ProviderErrors { get; }
}

/// <summary>
/// A write was sent but no answer came back (timeout / connection drop): it may or may not have taken effect.
/// </summary>
public class BillingOutcomeUnknownException : BillingProviderException
{
    public BillingOutcomeUnknownException(BillingFailureKind kind, string message, Exception? innerException = null)
        : base(kind, message, null, null, innerException)
    {
    }
}
