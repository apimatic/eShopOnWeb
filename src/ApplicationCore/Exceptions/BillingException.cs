using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for every failure raised by the subscription-billing capability. Callers can catch
/// this to keep billing faults from being mistaken for ordinary storefront errors.
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
/// The billing provider is not configured for this deployment - the <c>Maxio:</c> configuration
/// section is missing or incomplete. Surfaced as 503 rather than 500: the request was fine, the
/// deployment is not.
/// </summary>
public sealed class BillingNotConfiguredException : BillingException
{
    public BillingNotConfiguredException(string message) : base(message)
    {
    }
}

/// <summary>The requested plan is not offered by the configured product family.</summary>
public sealed class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle, string productFamilyHandle)
        : base($"No subscription plan with handle '{planHandle}' is offered by product family '{productFamilyHandle}'.")
    {
        PlanHandle = planHandle;
        ProductFamilyHandle = productFamilyHandle;
    }

    public string PlanHandle { get; }
    public string ProductFamilyHandle { get; }
}

/// <summary>
/// The provider recognised this request as a replay of one it already accepted, but the resulting
/// subscription could not be located. The original request may or may not have succeeded, so the
/// caller must re-read rather than retry.
/// </summary>
public sealed class DuplicateBillingRequestException : BillingException
{
    public DuplicateBillingRequestException(string message) : base(message)
    {
    }
}

/// <summary>
/// The provider rejected the request as invalid (for example a plan that requires a payment
/// method the shopper has not supplied).
/// </summary>
public sealed class BillingValidationException : BillingException
{
    public BillingValidationException(string message, IEnumerable<string>? errors = null)
        : base(message)
    {
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>The provider was unreachable, timed out, or returned an unusable response.</summary>
public sealed class BillingProviderException : BillingException
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
