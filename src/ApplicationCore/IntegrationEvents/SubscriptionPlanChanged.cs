using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription has been moved to a different plan (plan.md UC3).
/// Delivery is best-effort (plan.md §2.5).
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userName, int subscriptionId, string previousPlanHandle,
        string newPlanHandle, decimal prorationAmount, bool appliedImmediately)
    {
        UserName = userName;
        SubscriptionId = subscriptionId;
        PreviousPlanHandle = previousPlanHandle;
        NewPlanHandle = newPlanHandle;
        ProrationAmount = prorationAmount;
        AppliedImmediately = appliedImmediately;
    }

    public string UserName { get; }

    public int SubscriptionId { get; }

    public string PreviousPlanHandle { get; }

    public string NewPlanHandle { get; }

    /// <summary>Net prorated charge (positive) or credit (negative) in dollars; zero for a deferred change.</summary>
    public decimal ProrationAmount { get; }

    /// <summary>True when the change took effect immediately; false when deferred to the next renewal.</summary>
    public bool AppliedImmediately { get; }
}
