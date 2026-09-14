using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// How a billing-provider failure should be presented to the caller. Distinct kinds are kept distinct so
/// that "you sent something invalid" never looks like "the provider is down".
/// </summary>
public enum BillingFailureKind
{
    /// <summary>The provider rejected the request because of something the caller supplied.</summary>
    Rejected,

    /// <summary>The requested resource does not exist at the provider.</summary>
    NotFound,

    /// <summary>The request conflicts with state that already exists.</summary>
    Conflict,

    /// <summary>The provider is unreachable, timed out, or answered 5xx.</summary>
    Unavailable,

    /// <summary>Our own credentials or catalog configuration are wrong. Never the caller's fault.</summary>
    Misconfigured,

    /// <summary>The provider answered, but the outcome could not be determined.</summary>
    Unknown
}

/// <summary>
/// The single failure type the billing integration surfaces. Its <see cref="Exception.Message"/> is always
/// safe to return to an API caller — provider and framework exception text is logged, never propagated.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(BillingFailureKind kind, string message,
        HttpStatusCode? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    public BillingFailureKind Kind { get; }

    /// <summary>The HTTP status the provider returned, when one was available.</summary>
    public HttpStatusCode? ProviderStatusCode { get; }
}
