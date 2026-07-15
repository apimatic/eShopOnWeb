using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after a subscription's plan change commits successfully (UC3).</summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userId, int subscriptionId, string oldProductHandle, string newProductHandle, bool appliedImmediately)
    {
        UserId = userId;
        SubscriptionId = subscriptionId;
        OldProductHandle = oldProductHandle;
        NewProductHandle = newProductHandle;
        AppliedImmediately = appliedImmediately;
    }

    public string UserId { get; }
    public int SubscriptionId { get; }
    public string OldProductHandle { get; }
    public string NewProductHandle { get; }
    public bool AppliedImmediately { get; }
}
