using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan handle does not exist in the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle, IEnumerable<string>? availableHandles = null)
        : base(BuildMessage(planHandle, availableHandles))
    {
        PlanHandle = planHandle;
        AvailableHandles = availableHandles?.ToList() ?? new List<string>();
    }

    public string PlanHandle { get; }

    public IReadOnlyCollection<string> AvailableHandles { get; }

    private static string BuildMessage(string planHandle, IEnumerable<string>? availableHandles)
    {
        var available = availableHandles?.ToList() ?? new List<string>();
        return available.Count > 0
            ? $"No subscription plan with handle '{planHandle}' is available. Available plans: {string.Join(", ", available)}."
            : $"No subscription plan with handle '{planHandle}' is available.";
    }
}
