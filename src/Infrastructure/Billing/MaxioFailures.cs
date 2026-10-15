using System;
using System.Collections.Generic;
using System.Net;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// The one place SDK failures become <see cref="BillingProviderException"/>s with caller-safe messages.
/// Typed error bodies are read in each operation's own catch; this handles status codes and the non-API failures.
/// </summary>
internal static class MaxioFailures
{
    /// <param name="callerSuppliedInput">
    /// True only for writes whose payload the caller influenced; a 4xx on a read of our own configuration is our fault,
    /// not the caller's, so it is reported as <see cref="BillingFailureKind.Misconfigured"/>.
    /// </param>
    public static BillingProviderException FromStatus(HttpStatusCode status, Exception inner,
        IReadOnlyList<string>? reasons = null, bool callerSuppliedInput = false)
    {
        var code = (int)status;
        return code switch
        {
            401 or 403 => new BillingProviderException(BillingFailureKind.Misconfigured,
                "The billing provider rejected this store's credentials.", code, innerException: inner),
            429 => new BillingProviderException(BillingFailureKind.Unavailable,
                "The billing provider is rate limiting requests. Please try again shortly.", code, innerException: inner),
            400 or 409 or 422 when callerSuppliedInput => new BillingProviderException(BillingFailureKind.Rejected,
                "The billing provider rejected the request.", code, reasons, inner),
            >= 400 and < 500 => new BillingProviderException(BillingFailureKind.Misconfigured,
                "The billing provider refused a request from this store.", code, reasons, inner),
            _ => new BillingProviderException(BillingFailureKind.Unavailable,
                "The billing provider is unavailable. Please try again later.", code, innerException: inner),
        };
    }

    /// <summary>Translates any SDK failure that was not handled by a more specific, typed catch.</summary>
    public static BillingProviderException FromSdk(SdkException exception) => exception switch
    {
        ResponseDeserializationException e => new BillingProviderException(BillingFailureKind.Unavailable,
            "The billing provider returned a response that could not be processed.", (int)e.StatusCode, innerException: e),
        ApiException e => FromStatus(e.StatusCode, e),
        SdkTimeoutException e => new BillingProviderException(BillingFailureKind.Unavailable,
            "The billing provider did not respond in time. Please try again later.", innerException: e),
        SdkConnectionException e => new BillingProviderException(BillingFailureKind.Unavailable,
            "The billing provider could not be reached. Please try again later.", innerException: e),
        AuthSchemeException e => new BillingProviderException(BillingFailureKind.Misconfigured,
            "The billing provider credentials could not be applied.", innerException: e),
        _ => new BillingProviderException(BillingFailureKind.Unavailable,
            "The billing provider request failed.", innerException: exception),
    };

    public static BillingProviderException BudgetExceeded(Exception inner) =>
        new(BillingFailureKind.Unavailable, "The billing provider did not respond in time. Please try again later.",
            innerException: inner);

    /// <summary>True for statuses where the provider may already have acted on a write.</summary>
    public static bool IsAmbiguousWriteStatus(HttpStatusCode status) => (int)status >= 500 || (int)status < 300;
}
