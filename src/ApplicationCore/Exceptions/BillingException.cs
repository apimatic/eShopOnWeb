using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The billing system rejected the request as invalid (caller can act on it).</summary>
    Validation,
    /// <summary>The requested plan/customer does not exist.</summary>
    NotFound,
    /// <summary>The billing system answered with an error or an unreadable response.</summary>
    ProviderError,
    /// <summary>The billing system could not be reached (network, DNS, timeout) or is not configured.</summary>
    Unavailable
}

/// <summary>
/// Failure talking to the billing system. The message is always safe to show to a caller.
/// </summary>
public class BillingException : Exception
{
    public BillingFailureKind Kind { get; }

    public BillingException(BillingFailureKind kind, string message, Exception? inner = null) : base(message, inner)
    {
        Kind = kind;
    }
}
