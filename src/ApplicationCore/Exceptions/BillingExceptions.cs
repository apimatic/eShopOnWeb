using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Base type for every failure raised by the subscription billing integration.</summary>
public abstract class BillingException : Exception
{
    protected BillingException(string message) : base(message)
    {
    }

    protected BillingException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The billing provider is not configured (or is misconfigured) on this host, so no billing call
/// can be attempted. Surfaced to callers as <c>503 Service Unavailable</c>.
/// </summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// The billing provider rejected the request as invalid. This is the caller's fault, so it is
/// surfaced as <c>400 Bad Request</c> along with the provider's own messages.
/// </summary>
public class BillingValidationException : BillingException
{
    public BillingValidationException(string message, IEnumerable<string>? errors = null)
        : base(message)
    {
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>The provider's validation messages, verbatim.</summary>
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>The requested plan is not offered by the configured product family.</summary>
public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// The billing provider could not be reached, or answered in a way this application cannot use.
/// Surfaced as <c>502 Bad Gateway</c> (or <c>504</c> on timeout) because the caller did nothing wrong.
/// </summary>
public class BillingProviderException : BillingException
{
    public BillingProviderException(string message, Exception? innerException = null, int? statusCode = null, bool isTimeout = false)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        IsTimeout = isTimeout;
    }

    /// <summary>HTTP status the provider returned, when there was a response.</summary>
    public int? StatusCode { get; }

    /// <summary>True when the call timed out or the connection failed before a response arrived.</summary>
    public bool IsTimeout { get; }
}
