using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    /// <summary>The provider rejected the request's content (validation); the caller may be able to fix it.</summary>
    Rejected,
    /// <summary>Our credentials, site or catalog configuration are wrong; not the caller's fault.</summary>
    Misconfigured,
    /// <summary>Our provider quota is exhausted.</summary>
    Throttled,
    /// <summary>The provider is unreachable, timed out or failed (5xx).</summary>
    Unavailable,
    /// <summary>The provider answered with a body we could not read.</summary>
    UnreadableResponse
}

/// <summary>A billing provider failure, translated at the gateway boundary. Messages are caller-safe.</summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(BillingFailureKind kind, string message, int? providerStatusCode = null,
        IReadOnlyList<string>? providerMessages = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        ProviderMessages = providerMessages ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }
    public int? ProviderStatusCode { get; }
    public IReadOnlyList<string> ProviderMessages { get; }
}

/// <summary>A provider write may or may not have landed and a re-read could not settle it.</summary>
public class BillingOutcomeUnknownException : Exception
{
    public BillingOutcomeUnknownException(string message, Exception? innerException = null) : base(message, innerException) { }
}

public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle)
        : base($"'{planHandle}' is not an available subscription plan.") { }
}

/// <summary>The buyer already holds a subscription that conflicts with the request, or an enrollment is in progress.</summary>
public class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message) : base(message) { }
}
