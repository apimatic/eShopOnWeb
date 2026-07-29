using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Raised when the billing system rejects a request or a requested plan/customer cannot be
/// resolved. Carries the provider's error messages so callers can surface actionable detail.
/// </summary>
public class SubscriptionException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public SubscriptionException(string message, IEnumerable<string>? errors = null)
        : base(message)
    {
        Errors = errors?.ToList() ?? new List<string>();
    }

    public SubscriptionException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = new List<string>();
    }
}

/// <summary>
/// Raised when a requested plan handle is not part of the configured product family.
/// </summary>
public sealed class PlanNotFoundException : SubscriptionException
{
    public PlanNotFoundException(string planHandle)
        : base($"No plan with handle '{planHandle}' is available.")
    {
    }
}
