using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingErrorKind
{
    /// <summary>The caller's request is malformed.</summary>
    InvalidRequest,

    /// <summary>The requested plan is not one this store offers.</summary>
    PlanNotAvailable,

    /// <summary>The billing system refused the caller's request (validation).</summary>
    Rejected,

    /// <summary>The request conflicts with the buyer's existing subscription.</summary>
    Conflict,

    /// <summary>An identical request for this buyer is still being processed.</summary>
    InProgress,

    /// <summary>The billing system could not be reached or did not answer in time; retrying later may succeed.</summary>
    ProviderUnavailable,

    /// <summary>The billing system answered in a way this app cannot use (credentials, configuration, unexpected payload).</summary>
    ProviderError
}

/// <summary>
/// The single failure type the subscription capability raises. Messages are safe to show to the caller:
/// they never carry provider payloads, URLs, credentials or SDK type names.
/// </summary>
public class BillingException : Exception
{
    public BillingException(BillingErrorKind kind, string message, IReadOnlyList<string>? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Details = details ?? [];
    }

    public BillingErrorKind Kind { get; }

    /// <summary>Caller-actionable messages from the billing system (for example validation errors).</summary>
    public IReadOnlyList<string> Details { get; }
}
