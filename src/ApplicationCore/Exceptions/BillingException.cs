using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for every failure surfaced by the subscription-billing capability.
/// </summary>
public abstract class BillingException : Exception
{
    protected BillingException(string message) : base(message)
    {
    }

    protected BillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Subscription billing is not configured on this deployment, so the capability is unavailable.
/// </summary>
public class BillingNotConfiguredException : BillingException
{
    public BillingNotConfiguredException(IEnumerable<string> missingSettings)
        : base($"Subscription billing is not configured. Missing or empty configuration: {string.Join(", ", missingSettings)}.")
    {
        MissingSettings = missingSettings.ToList();
    }

    /// <summary>Names (never values) of the configuration keys that need to be supplied.</summary>
    public IReadOnlyList<string> MissingSettings { get; }
}

/// <summary>
/// The requested plan is not offered by this deployment.
/// </summary>
public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle, string productFamilyHandle)
        : base($"No subscription plan with handle '{planHandle}' is offered in product family '{productFamilyHandle}'.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// The request cannot be satisfied because the idempotency key it maps to was already consumed by
/// a subscription that is no longer live.
/// </summary>
public class SubscriptionConflictException : BillingException
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// The billing provider refused the request as invalid. The caller has to change something.
/// </summary>
public class BillingRequestRejectedException : BillingException
{
    public BillingRequestRejectedException(string message, IEnumerable<string>? providerErrors = null)
        : base(message)
    {
        ProviderErrors = providerErrors?.ToList() ?? new List<string>();
    }

    /// <summary>Verbatim error messages returned by the provider, when it supplied any.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }
}

/// <summary>
/// The billing provider failed or could not be reached. Not the caller's fault.
/// </summary>
public class BillingProviderException : BillingException
{
    public BillingProviderException(string message, IEnumerable<string>? providerErrors = null)
        : base(message)
    {
        ProviderErrors = providerErrors?.ToList() ?? new List<string>();
    }

    public BillingProviderException(string message, Exception innerException, IEnumerable<string>? providerErrors = null)
        : base(message, innerException)
    {
        ProviderErrors = providerErrors?.ToList() ?? new List<string>();
    }

    /// <summary>Verbatim error messages returned by the provider, when it supplied any.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }
}
