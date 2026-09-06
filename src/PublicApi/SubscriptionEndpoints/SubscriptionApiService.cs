using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps the authenticated caller onto a <see cref="Subscriber"/> and forwards to the billing
/// capability. The caller can only ever act on their own billing account: the identity comes
/// from the bearer token, and nothing in the request body can change it.
/// </summary>
public class SubscriptionApiService : ISubscriptionApiService
{
    private readonly ISubscriptionService _subscriptions;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionApiService(ISubscriptionService subscriptions, UserManager<ApplicationUser> userManager)
    {
        _subscriptions = subscriptions;
        _userManager = userManager;
    }

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _subscriptions.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(
        ClaimsPrincipal principal,
        string? planHandle,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var subscriber = await ResolveSubscriberAsync(principal);

        var request = new SubscribeRequest
        {
            Subscriber = subscriber,
            PlanHandle = string.IsNullOrWhiteSpace(planHandle) ? null : planHandle!.Trim(),
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey!.Trim()
        };

        return await _subscriptions.SubscribeAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var subscriber = await ResolveSubscriberAsync(principal);
        return await _subscriptions.ListSubscriptionsAsync(subscriber, cancellationToken);
    }

    private async Task<Subscriber> ResolveSubscriberAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new UnknownSubscriberException("The access token does not identify a user.");
        }

        var user = await _userManager.FindByNameAsync(userName!);
        if (user is null)
        {
            throw new UnknownSubscriberException($"User '{userName}' no longer exists.");
        }

        return new Subscriber
        {
            UserId = user.Id,
            UserName = user.UserName ?? userName!,
            Email = user.Email ?? user.UserName ?? userName!
        };
    }
}
