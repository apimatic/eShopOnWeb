using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The caller's request is invalid (e.g. an unknown plan).</summary>
    InvalidRequest,
    /// <summary>The billing provider refused the request on its merits (validation).</summary>
    Rejected,
    /// <summary>The request conflicts with one already in progress or an existing subscription.</summary>
    Conflict,
    /// <summary>The billing provider is unreachable, misconfigured or failing.</summary>
    Unavailable
}

/// <summary>
/// A billing failure carrying a caller-safe message. Never wraps provider payloads in the message.
/// </summary>
public class BillingException : Exception
{
    public BillingException(BillingFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public BillingFailureKind Kind { get; }
}
