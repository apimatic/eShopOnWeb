using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single exception type raised by <see cref="Interfaces.IBillingClient"/> for every failure
/// mode of the billing provider (validation, not-found, provider rejection, or the provider being
/// unreachable). ApplicationCore and above never see a provider-specific exception type.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, BillingErrorKind kind, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public BillingErrorKind Kind { get; }
    public int? StatusCode { get; }
}

/// <summary>Provider-agnostic classification of a <see cref="BillingProviderException"/>.</summary>
public enum BillingErrorKind
{
    /// <summary>The provider rejected the request as invalid (e.g. a 422 validation error).</summary>
    Validation,

    /// <summary>The requested resource does not exist on the provider (e.g. a 404).</summary>
    NotFound,

    /// <summary>The provider reachable but refused the operation for a business reason (e.g. an illegal state transition).</summary>
    ProviderRejected,

    /// <summary>The provider could not be reached at all (network/timeout failure).</summary>
    ConnectionFailure
}
