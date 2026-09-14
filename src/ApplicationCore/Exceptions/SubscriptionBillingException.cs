using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for every failure raised by the subscription billing integration. Carries the
/// provider-reported detail so the API surface can hand callers something actionable instead of
/// leaking transport-level exceptions.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, IEnumerable<string>? providerErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderErrors = providerErrors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray() ?? Array.Empty<string>();
    }

    /// <summary>Verbatim error messages returned by the billing provider, if any.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }
}

/// <summary>
/// The billing provider is not configured for this deployment, so the subscription capability is off.
/// </summary>
public class SubscriptionBillingNotConfiguredException : SubscriptionBillingException
{
    public SubscriptionBillingNotConfiguredException(string message)
        : base(message)
    {
    }
}

/// <summary>The requested plan does not exist in the configured catalog.</summary>
public class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string planHandle, string productFamilyHandle)
        : base($"Subscription plan '{planHandle}' was not found in product family '{productFamilyHandle}'.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// The billing provider rejected the request as invalid (for example, the plan requires a payment
/// method that was not supplied). Retrying the same request unchanged will fail the same way.
/// </summary>
public class SubscriptionBillingRejectedException : SubscriptionBillingException
{
    public SubscriptionBillingRejectedException(string message, IEnumerable<string>? providerErrors = null, Exception? innerException = null)
        : base(message, providerErrors, innerException)
    {
    }
}

/// <summary>
/// The billing provider could not be reached or failed in a way that may succeed on retry.
/// </summary>
public class SubscriptionBillingUnavailableException : SubscriptionBillingException
{
    public SubscriptionBillingUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException: innerException)
    {
    }
}
