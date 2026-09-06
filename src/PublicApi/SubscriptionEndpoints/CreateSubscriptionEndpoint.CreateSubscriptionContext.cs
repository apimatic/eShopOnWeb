using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The subscribe request plus the context the route delegate resolves for it: the authenticated
/// subscriber and the request's cancellation token. Built by the delegate, never model-bound.
/// </summary>
public class CreateSubscriptionContext : BaseRequest
{
    public CreateSubscriptionContext(CreateSubscriptionRequest request, Subscriber subscriber, CancellationToken cancellationToken)
    {
        Request = request;
        Subscriber = subscriber;
        CancellationToken = cancellationToken;
    }

    public CreateSubscriptionRequest Request { get; }

    public Subscriber Subscriber { get; }

    public CancellationToken CancellationToken { get; }
}
