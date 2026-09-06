using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// How a subscription-billing call failed. The kind - not the provider's raw status - is what the API
/// boundary maps onto an HTTP response, so that distinct failures stay distinct for our own callers
/// while provider-internal detail never leaks out.
/// </summary>
public enum SubscriptionBillingFailure
{
    /// <summary>The request was rejected as invalid by the billing provider (e.g. HTTP 422).</summary>
    InvalidRequest,

    /// <summary>The plan, customer, or product family the request named does not exist.</summary>
    NotFound,

    /// <summary>
    /// The billing provider rejected our credentials or site configuration (HTTP 401/403). This is our
    /// misconfiguration, never the API caller's - it must not surface to them as an authentication error.
    /// </summary>
    ProviderMisconfigured,

    /// <summary>The provider was unreachable, timed out, throttled us, or returned a 5xx.</summary>
    ProviderUnavailable,

    /// <summary>
    /// The provider answered but the payload could not be read, so the outcome of the call is genuinely
    /// unknown. Never convert this into a domain absence - "I could not read the answer" is not "no".
    /// </summary>
    ProviderResponseUnreadable,

    /// <summary>
    /// A write may or may not have reached the provider (a transport failure, or a re-send our write-once
    /// guard refused). The caller should re-read state rather than blindly retrying.
    /// </summary>
    OutcomeUnknown,

    /// <summary>Subscription billing is not configured in this deployment.</summary>
    NotConfigured
}

/// <summary>
/// The single failure type the subscription-billing abstraction raises. Every provider exception,
/// transport failure, and unreadable payload is translated into this one type at the adapter boundary so
/// that endpoints have exactly one thing to handle.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        SubscriptionBillingFailure failure,
        string message,
        int? providerStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        ProviderStatusCode = providerStatusCode;
    }

    public SubscriptionBillingFailure Failure { get; }

    /// <summary>The provider's HTTP status, when one was available. Diagnostic only - never returned verbatim.</summary>
    public int? ProviderStatusCode { get; }
}
