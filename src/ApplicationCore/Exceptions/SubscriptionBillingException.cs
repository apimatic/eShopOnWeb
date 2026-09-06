using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for every failure of the recurring-subscription billing capability. Callers - and the
/// API exception middleware - can catch this without taking a dependency on the billing provider.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message)
        : base(message)
    {
    }

    public SubscriptionBillingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The requested plan is not part of the configured catalog.
/// </summary>
public class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// The billing system rejected the request as invalid (for example a missing required attribute).
/// Retrying the same request unchanged will fail again.
/// </summary>
public class SubscriptionBillingValidationException : SubscriptionBillingException
{
    public SubscriptionBillingValidationException(IReadOnlyList<string> errors, Exception? innerException = null)
        : base(errors.Count > 0
            ? $"The billing system rejected the request: {string.Join("; ", errors)}"
            : "The billing system rejected the request.", innerException!)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// Subscription billing is not configured on this host - the Maxio settings are missing or invalid.
/// </summary>
public class SubscriptionBillingConfigurationException : SubscriptionBillingException
{
    public SubscriptionBillingConfigurationException(IEnumerable<string> problems)
        : base("Subscription billing is not configured: " + string.Join(" ", problems.Select(p => p.TrimEnd('.') + ".")))
    {
    }

    public SubscriptionBillingConfigurationException(string problem)
        : this(new[] { problem })
    {
    }
}
