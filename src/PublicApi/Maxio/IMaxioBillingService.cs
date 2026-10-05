using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The identity of the logged-in eShopOnWeb shopper, resolved from the JWT by the
/// endpoints and passed into the billing service. UserId is the stable identity id
/// used as the Maxio customer reference.
/// </summary>
public sealed record MaxioSubscriberIdentity(string UserId, string UserName, string? Email);

public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscription plans (Maxio products) available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper (idempotent), then subscribes them
    /// to the requested plan (idempotent per user+plan). Returns the subscription state.
    /// </summary>
    Task<MaxioSubscriptionDto> SubscribeAsync(MaxioSubscriberIdentity subscriber, string productHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the shopper's Maxio subscriptions. Returns an empty list when no Maxio
    /// customer exists for the shopper yet.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscriptionDto>> ListMySubscriptionsAsync(MaxioSubscriberIdentity subscriber, CancellationToken cancellationToken);
}