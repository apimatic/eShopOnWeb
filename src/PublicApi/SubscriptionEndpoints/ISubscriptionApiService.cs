using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The subscription capability as the API surface needs it: identical to
/// <see cref="ApplicationCore.Interfaces.ISubscriptionService"/> except that the shopper is
/// identified by the authenticated principal, never by request input.
/// </summary>
public interface ISubscriptionApiService
{
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(
        ClaimsPrincipal principal,
        string? planHandle,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}
