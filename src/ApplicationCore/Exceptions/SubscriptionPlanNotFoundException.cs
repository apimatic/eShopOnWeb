using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan handle is not offered by the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle, IEnumerable<string> availableHandles)
        : base(BuildMessage(planHandle, availableHandles))
    {
        PlanHandle = planHandle;
        AvailableHandles = availableHandles.ToArray();
    }

    public string PlanHandle { get; }

    public IReadOnlyList<string> AvailableHandles { get; }

    private static string BuildMessage(string planHandle, IEnumerable<string> availableHandles)
    {
        var available = string.Join(", ", availableHandles);
        return string.IsNullOrEmpty(available)
            ? $"Subscription plan '{planHandle}' was not found and no plans are currently available."
            : $"Subscription plan '{planHandle}' was not found. Available plans: {available}.";
    }
}
