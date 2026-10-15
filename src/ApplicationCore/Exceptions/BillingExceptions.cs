using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Base type for failures reported by the billing system. Messages are safe to return to callers.</summary>
public abstract class BillingProviderException : Exception
{
    protected BillingProviderException(string message, int? providerStatusCode, Exception? innerException)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>The HTTP status the billing system answered with, when it answered at all.</summary>
    public int? ProviderStatusCode { get; }
}

/// <summary>The billing system rejected the request (validation, conflict); retrying the same request will not help.</summary>
public class BillingRequestRejectedException : BillingProviderException
{
    public BillingRequestRejectedException(string message, IReadOnlyList<string> errors, int? providerStatusCode, Exception? innerException = null)
        : base(message, providerStatusCode, innerException)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>The billing system could not be reached or could not serve the request; nothing was changed.</summary>
public class BillingProviderUnavailableException : BillingProviderException
{
    public BillingProviderUnavailableException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, providerStatusCode, innerException)
    {
    }
}

/// <summary>A write failed after it may have reached the billing system; its outcome must be settled by re-reading.</summary>
public class BillingOutcomeUnknownException : BillingProviderException
{
    public BillingOutcomeUnknownException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, providerStatusCode, innerException)
    {
    }
}

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is offered.")
    {
    }
}

/// <summary>Another request for the same buyer is already being processed.</summary>
public class SubscriptionInProgressException : Exception
{
    public SubscriptionInProgressException(string message) : base(message)
    {
    }
}
