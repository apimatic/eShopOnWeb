using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The subscribe request after the caller's identity has been resolved from the bearer token. Keeping this
/// separate from <see cref="CreateSubscriptionRequest"/> means the subscriber can never be supplied on the
/// wire.
/// </summary>
public class SubscribeCommand : BaseRequest
{
    public SubscribeCommand(SubscriberIdentity subscriber, string? planHandle)
    {
        Subscriber = subscriber;
        PlanHandle = planHandle;
    }

    public SubscriberIdentity Subscriber { get; }

    public string? PlanHandle { get; }
}
