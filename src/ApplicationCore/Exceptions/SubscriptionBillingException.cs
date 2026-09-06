using System;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the billing integration surfaces. Every provider failure — an error
/// status, an unreachable host, a response that could not be read — is translated into this at the
/// integration boundary, so callers have one type to handle and no provider detail leaks outward.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(BillingFailureKind kind,
        string message,
        HttpStatusCode? providerStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    public BillingFailureKind Kind { get; }

    /// <summary>
    /// Status the provider returned, when there was one. Null for transport failures and for
    /// rejections whose status the SDK discarded while parsing the error body.
    /// </summary>
    public HttpStatusCode? ProviderStatusCode { get; }
}
