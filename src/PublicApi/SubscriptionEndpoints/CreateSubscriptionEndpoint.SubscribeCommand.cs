using System;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// What the endpoint actually handles: the request body fused with the identity taken from the bearer
/// token. Kept separate from <see cref="CreateSubscriptionRequest"/> so nothing identity-related can
/// ever be bound from the wire.
/// </summary>
public class SubscribeCommand
{
    public SubscribeCommand(Guid correlationId, SubscribeRequest subscribeRequest, CancellationToken cancellationToken)
    {
        CorrelationId = correlationId;
        SubscribeRequest = subscribeRequest;
        CancellationToken = cancellationToken;
    }

    public Guid CorrelationId { get; }

    public SubscribeRequest SubscribeRequest { get; }

    public CancellationToken CancellationToken { get; }
}
