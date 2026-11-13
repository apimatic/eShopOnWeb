using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for subscription-billing failures. Messages are caller-safe: they never carry provider
/// payloads, SDK type names, URLs or credentials.
/// </summary>
public abstract class BillingException : Exception
{
    protected BillingException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

/// <summary>The requested plan is not one this application offers (400).</summary>
public sealed class SubscriptionPlanNotOfferedException : BillingException
{
    public SubscriptionPlanNotOfferedException(string planHandle)
        : base($"Plan '{planHandle}' is not an available subscription plan.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>An identical request for the same user is already being processed (409).</summary>
public sealed class SubscriptionRequestInProgressException : BillingException
{
    public SubscriptionRequestInProgressException(string message) : base(message)
    {
    }
}

/// <summary>The billing system rejected the request as invalid (422).</summary>
public sealed class BillingRequestRejectedException : BillingException
{
    public BillingRequestRejectedException(IReadOnlyList<string> errors, Exception? innerException = null)
        : base("The billing system rejected the request.", innerException)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// The billing system is temporarily unreachable, timed out, throttled us or returned a server error (503).
/// The caller may retry.
/// </summary>
public sealed class BillingUnavailableException : BillingException
{
    public BillingUnavailableException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

/// <summary>
/// The billing integration failed in a way the caller cannot fix — our credentials/configuration were
/// refused or the provider returned something we could not process (502).
/// </summary>
public sealed class BillingIntegrationException : BillingException
{
    public BillingIntegrationException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}
