using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <inheritdoc cref="ISubscriptionApiService"/>
public class SubscriptionApiService : ISubscriptionApiService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionBillingService _billingService;
    private readonly ILogger<SubscriptionApiService> _logger;

    public SubscriptionApiService(
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService billingService,
        ILogger<SubscriptionApiService> logger)
    {
        _userManager = userManager;
        _billingService = billingService;
        _logger = logger;
    }

    public async Task<Subscriber?> ResolveSubscriberAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        // The PublicApi bearer token carries the user name (and roles) and nothing else, so the
        // account is re-read here to pick up the e-mail address the billing customer is created with.
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            _logger.LogWarning("Bearer token names user {UserName}, which no longer exists.", userName);
            return null;
        }

        // The user name is eShopOnWeb's stable business key for an account: it is what the token
        // carries, and unlike the Identity row id it survives a re-seed of the identity store, which
        // matters when the host runs on the in-memory database.
        var userKey = user.NormalizedUserName ?? user.UserName!;

        return Subscriber.ForUser(userKey, user.Email ?? user.UserName!);
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingService.ListPlansAsync(cancellationToken);

    public Task<SubscriptionEnrollment> SubscribeAsync(
        Subscriber subscriber,
        string planHandle,
        CancellationToken cancellationToken = default) =>
        _billingService.SubscribeAsync(subscriber, planHandle, cancellationToken);

    public Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default) =>
        _billingService.ListSubscriptionsAsync(subscriber, cancellationToken);
}
