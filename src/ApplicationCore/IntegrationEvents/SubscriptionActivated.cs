using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a customer has been successfully enrolled in a plan (plan.md UC1).
/// Delivery is best-effort: a failing handler never rolls back the enrollment (plan.md §2.5).
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userName, int subscriptionId, string planHandle, decimal planPrice, string state)
    {
        UserName = userName;
        SubscriptionId = subscriptionId;
        PlanHandle = planHandle;
        PlanPrice = planPrice;
        State = state;
    }

    /// <summary>The eShopOnWeb user (email / username) the subscription belongs to.</summary>
    public string UserName { get; }

    public int SubscriptionId { get; }

    public string PlanHandle { get; }

    /// <summary>Recurring plan price in dollars.</summary>
    public decimal PlanPrice { get; }

    public string State { get; }
}
