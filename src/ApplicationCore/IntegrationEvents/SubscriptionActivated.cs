using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a customer is successfully enrolled in a plan. Delivery is best-effort:
/// a handler failure never rolls the subscription back.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userReference, int subscriptionId, string? planHandle)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        PlanHandle = planHandle;
    }

    public string UserReference { get; }

    public int SubscriptionId { get; }

    public string? PlanHandle { get; }
}
