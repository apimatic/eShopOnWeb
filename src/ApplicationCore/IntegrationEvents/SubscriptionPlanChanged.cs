using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process (best-effort) after a subscription's plan change has been committed with the billing provider.</summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userReference, int subscriptionId, string fromProductHandle, string toProductHandle, bool appliedAtRenewal)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        FromProductHandle = fromProductHandle;
        ToProductHandle = toProductHandle;
        AppliedAtRenewal = appliedAtRenewal;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public string FromProductHandle { get; }
    public string ToProductHandle { get; }
    public bool AppliedAtRenewal { get; }
}
