using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <inheritdoc cref="ISubscriptionService"/>
public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(IMaxioBillingService billing, UserManager<ApplicationUser> userManager)
    {
        _billing = billing;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _billing.GetPlansAsync(cancellationToken);
        return plans.Select(MapPlan).ToList();
    }

    public async Task<CustomerSubscriptionDto> SubscribeAsync(ClaimsPrincipal user, string planHandle, CancellationToken cancellationToken = default)
    {
        var subscriber = await ResolveSubscriberAsync(user);
        var subscription = await _billing.SubscribeAsync(subscriber, planHandle, cancellationToken);
        return MapSubscription(subscription);
    }

    public async Task<IReadOnlyList<CustomerSubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var subscriber = await ResolveSubscriberAsync(user);
        var subscriptions = await _billing.GetSubscriptionsAsync(subscriber, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    /// <summary>
    /// Builds a <see cref="BillingSubscriber"/> from the authenticated principal. The subscriber's
    /// stable id (used as the Maxio customer reference) comes from the ASP.NET Identity user, not
    /// from the token directly, so it stays consistent regardless of token contents.
    /// </summary>
    private async Task<BillingSubscriber> ResolveSubscriberAsync(ClaimsPrincipal user)
    {
        var userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new UnauthorizedAccessException("The access token does not identify a user.");
        }

        var appUser = await _userManager.FindByNameAsync(userName);
        if (appUser is null)
        {
            throw new UnauthorizedAccessException($"No account was found for '{userName}'.");
        }

        var email = appUser.Email ?? userName;
        return new BillingSubscriber(appUser.Id, email);
    }

    private static SubscriptionPlanDto MapPlan(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        IntervalUnit = plan.IntervalUnit,
        IntervalCount = plan.IntervalCount,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    private static CustomerSubscriptionDto MapSubscription(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        IntervalUnit = subscription.IntervalUnit,
        IntervalCount = subscription.IntervalCount,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingAt = subscription.NextBillingAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        AlreadyExisted = subscription.AlreadyExisted
    };
}
