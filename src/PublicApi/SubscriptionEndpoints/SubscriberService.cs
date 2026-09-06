using System;
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

/// <summary>
/// Performs subscription work on behalf of the caller identified by the bearer token.
/// </summary>
/// <remarks>
/// The subscriber is never taken from the request body - only from the validated token - so one
/// shopper cannot enroll or inspect another.
/// </remarks>
public class SubscriberService
{
    private readonly ISubscriptionService _subscriptions;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriberService> _logger;

    public SubscriberService(
        ISubscriptionService subscriptions,
        UserManager<ApplicationUser> userManager,
        ILogger<SubscriberService> logger)
    {
        _subscriptions = subscriptions;
        _userManager = userManager;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        _subscriptions.GetPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(
        ClaimsPrincipal caller,
        string planHandle,
        string? paymentCollectionMethod,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var subscriber = await ResolveAsync(caller);

        return await _subscriptions.SubscribeAsync(
            new SubscribeRequest(subscriber, planHandle, paymentCollectionMethod, idempotencyKey),
            cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(ClaimsPrincipal caller, CancellationToken cancellationToken = default)
    {
        var subscriber = await ResolveAsync(caller);

        return await _subscriptions.GetSubscriptionsAsync(subscriber, cancellationToken);
    }

    private async Task<SubscriberIdentity> ResolveAsync(ClaimsPrincipal caller)
    {
        var userName = caller?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new UnauthorizedAccessException("The bearer token does not identify a user.");
        }

        var user = await _userManager.FindByNameAsync(userName!);
        if (user is null)
        {
            // The token is validly signed but the local identity store has no matching row - for
            // example after the in-memory database was recreated. The token remains the authority
            // on who is calling, so we fall back to its user name.
            _logger.LogWarning("No local identity record for {UserName}; using the token's user name for billing.", userName);
        }

        return new SubscriberIdentity(userName!, user?.Email ?? userName);
    }
}
