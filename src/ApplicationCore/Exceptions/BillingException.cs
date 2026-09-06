using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for failures raised by the recurring billing integration.
/// </summary>
public abstract class BillingException : Exception
{
    protected BillingException(string message, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Errors = errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray() ?? Array.Empty<string>();
    }

    /// <summary>Detail messages reported by the billing system, when it supplied any.</summary>
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// The billing integration is not configured (missing API key / subdomain). The deployment cannot
/// serve subscription traffic until configuration is supplied.
/// </summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// The billing system rejected the request as invalid (e.g. HTTP 422 with a list of validation
/// errors). Retrying the same request unchanged will fail again.
/// </summary>
public class BillingRequestInvalidException : BillingException
{
    public BillingRequestInvalidException(string message, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, errors, innerException)
    {
    }
}

/// <summary>
/// The billing system was unreachable or returned an unexpected failure.
/// </summary>
public class BillingProviderException : BillingException
{
    public BillingProviderException(string message, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, errors, innerException)
    {
    }
}

/// <summary>
/// The requested plan handle does not exist in the configured billing catalog.
/// </summary>
public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the billing catalog.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
