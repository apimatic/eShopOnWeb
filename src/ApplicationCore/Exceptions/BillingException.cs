using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The request is invalid for this storefront (e.g. unknown plan). The caller can fix it.</summary>
    InvalidRequest,

    /// <summary>The billing provider rejected the request on its merits (validation).</summary>
    Rejected,

    /// <summary>The same operation is already in progress for this subscriber.</summary>
    Conflict,

    /// <summary>The billing provider could not be reached, timed out, failed, or refused our credentials.</summary>
    Unavailable
}

/// <summary>
/// A billing failure carrying only caller-safe text; provider details stay in <see cref="Exception.InnerException"/> and the logs.
/// </summary>
public class BillingException : Exception
{
    public BillingException(BillingFailureKind kind, string message, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Errors = errors ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }

    /// <summary>Validation messages returned by the provider, safe to show to the caller.</summary>
    public IReadOnlyList<string> Errors { get; }
}
