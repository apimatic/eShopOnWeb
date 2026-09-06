using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for failures that originate at the external billing provider. Infrastructure
/// translates provider-specific transport errors into one of the derived types so that the
/// application and API layers never depend on the provider client.
/// </summary>
public abstract class BillingProviderException : Exception
{
    protected BillingProviderException(string message, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>Messages reported by the provider, verbatim and safe to show to the caller.</summary>
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// The provider rejected the request as invalid (for example an unknown plan handle or a
/// validation failure on the customer). The caller can fix this by changing the request.
/// </summary>
public class BillingRequestRejectedException : BillingProviderException
{
    public BillingRequestRejectedException(string message, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, errors, innerException)
    {
    }
}

/// <summary>
/// The provider could not be reached, timed out, throttled us past our retry budget, or answered
/// with something this build cannot interpret. Retrying later may succeed.
/// </summary>
public class BillingProviderUnavailableException : BillingProviderException
{
    public BillingProviderUnavailableException(string message, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, errors, innerException)
    {
    }
}

/// <summary>
/// The provider refused our credentials, or the configured site/product family does not exist.
/// This is a deployment configuration fault, not a caller fault.
/// </summary>
public class BillingConfigurationException : BillingProviderException
{
    public BillingConfigurationException(string message, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, errors, innerException)
    {
    }
}

/// <summary>The requested plan handle is not offered by the configured product family.</summary>
public class SubscriptionPlanNotFoundException : BillingProviderException
{
    public SubscriptionPlanNotFoundException(string planHandle, IEnumerable<string>? availableHandles = null)
        : base(BuildMessage(planHandle, availableHandles))
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }

    private static string BuildMessage(string planHandle, IEnumerable<string>? availableHandles)
    {
        var available = availableHandles?.ToArray() ?? Array.Empty<string>();
        return available.Length == 0
            ? $"Subscription plan '{planHandle}' was not found."
            : $"Subscription plan '{planHandle}' was not found. Available plans: {string.Join(", ", available)}.";
    }
}
