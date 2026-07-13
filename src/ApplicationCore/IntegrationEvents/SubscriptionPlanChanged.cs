using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after a subscription's plan change (UC3) is committed with the provider.
/// Delivery is best-effort, in-process only (§2.5).
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string customerReference, long subscriptionId, string fromProductHandle, string toProductHandle, long proratedAdjustmentInCents)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        FromProductHandle = fromProductHandle;
        ToProductHandle = toProductHandle;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
    }

    public string CustomerReference { get; }
    public long SubscriptionId { get; }
    public string FromProductHandle { get; }
    public string ToProductHandle { get; }
    public long ProratedAdjustmentInCents { get; }
}
