using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Classifies a billing-provider failure so callers can map it to the right HTTP
/// outcome without leaking provider internals.
/// </summary>
public enum MaxioBillingErrorKind
{
    /// <summary>
    /// The provider deterministically rejected the request (4xx). Retrying cannot succeed.
    /// </summary>
    Rejected,

    /// <summary>
    /// The provider could not be reached, or the call timed out. Retrying may succeed.
    /// </summary>
    Unreachable,

    /// <summary>
    /// The provider answered with a body that could not be parsed. Outcome unknown.
    /// </summary>
    Unparseable,

    /// <summary>
    /// An unclassified provider failure.
    /// </summary>
    Unknown
}

/// <summary>
/// Raised when a Maxio Advanced Billing operation fails. Carries a caller-safe
/// message and the provider's HTTP status when one was observed.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(MaxioBillingErrorKind kind, string message, int? providerStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    public MaxioBillingErrorKind Kind { get; }

    /// <summary>
    /// The HTTP status the billing provider returned, when known.
    /// </summary>
    public int? ProviderStatusCode { get; }
}
