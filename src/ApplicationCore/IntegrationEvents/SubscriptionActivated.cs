using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, after a subscription is successfully enrolled with the
/// billing provider (UC1). See §2.5 of the integration plan: there is no durable outbox, so a
/// handler failure here never rolls back the subscription itself.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userReference, int subscriptionId, string planHandle)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        PlanHandle = planHandle;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public string PlanHandle { get; }
}
