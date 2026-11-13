using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A billing failure, already translated from the provider's own error types. <see cref="Exception.Message"/>
/// is safe to show to the caller; provider internals stay in <see cref="Exception.InnerException"/>.
/// </summary>
public class BillingException : Exception
{
    public BillingException(BillingFailure failure, string message, Exception? innerException = null,
        IReadOnlyList<string>? details = null, string? reference = null)
        : base(message, innerException)
    {
        Failure = failure;
        Details = details ?? Array.Empty<string>();
        Reference = reference;
    }

    public BillingFailure Failure { get; }

    /// <summary>Caller-actionable messages from the provider (e.g. validation errors).</summary>
    public IReadOnlyList<string> Details { get; }

    /// <summary>The reference of the write involved, when the outcome needs settling later.</summary>
    public string? Reference { get; }
}

public enum BillingFailure
{
    /// <summary>The requested plan is not one this store offers.</summary>
    InvalidPlan,
    /// <summary>The same operation is already in progress for this user.</summary>
    InProgress,
    /// <summary>The provider rejected the request as invalid.</summary>
    Rejected,
    /// <summary>The provider could not be reached, timed out or failed; nothing was changed.</summary>
    ProviderUnavailable,
    /// <summary>Our own provider credentials or configuration were refused.</summary>
    ProviderMisconfigured,
    /// <summary>The write may or may not have happened; the next request for it settles the outcome.</summary>
    OutcomeUnknown
}
