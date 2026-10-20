using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public enum BillingFailureKind
{
    /// <summary>The provider refused the caller's request (422). Provider messages are caller-safe.</summary>
    Rejected,

    /// <summary>Our credentials/configuration were refused, the provider failed (5xx) or answered unreadably.</summary>
    Unavailable,

    /// <summary>The provider throttled us.</summary>
    Throttled,

    /// <summary>The provider did not answer within the budget.</summary>
    Timeout,

    /// <summary>The plan list was cut short by the page cap, so a plan's absence cannot be proven.</summary>
    CatalogIncomplete
}

/// <summary>
/// The one failure type the billing gateway raises. <see cref="Exception.Message"/> is caller-safe:
/// it never carries an SDK message, a URL, a credential or personal data.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(BillingFailureKind kind, string safeMessage, int? providerStatus = null,
        IReadOnlyList<string>? providerMessages = null, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Kind = kind;
        ProviderStatus = providerStatus;
        ProviderMessages = providerMessages ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }

    /// <summary>HTTP status the provider answered with, when it answered.</summary>
    public int? ProviderStatus { get; }

    public IReadOnlyList<string> ProviderMessages { get; }
}

/// <summary>The caller's request is invalid (e.g. unknown or missing plan handle): 400.</summary>
public class InvalidSubscriptionRequestException : Exception
{
    public InvalidSubscriptionRequestException(string message) : base(message) { }
}

public enum BillingPendingKind
{
    /// <summary>Another request for the same reference is in flight right now (409).</summary>
    InFlight,

    /// <summary>A provider write's outcome is not yet known; it is settled on the next request (202).</summary>
    OutcomeUnknown
}

/// <summary>The operation has not failed: it is in progress or awaiting confirmation.</summary>
public class BillingOperationPendingException : Exception
{
    public BillingOperationPendingException(BillingPendingKind kind, string reference, string message) : base(message)
    {
        Kind = kind;
        Reference = reference;
    }

    public BillingPendingKind Kind { get; }

    /// <summary>The deterministic, PII-free reference of the pending write.</summary>
    public string Reference { get; }
}
