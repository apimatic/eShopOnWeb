using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(IMaxioAdvancedBillingClient maxio, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new SubscriptionPlanDto(product.Handle!, product.Name ?? product.Handle!, product.Description, product.PriceInCents, product.Interval, product.IntervalUnit ?? string.Empty))
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle) || planHandle.Length > 255)
        {
            throw new InvalidSubscriptionPlanException();
        }

        var user = await GetCurrentUserAsync(principal);
        var plans = await _maxio.ListProductsAsync(cancellationToken);
        var plan = plans.SingleOrDefault(product => string.Equals(product.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new InvalidSubscriptionPlanException();
        }

        var customer = await _maxio.EnsureCustomerAsync($"eshop-user-{user.Id}", user.Email ?? user.UserName ?? throw new CurrentUserNotFoundException(), cancellationToken);
        var existing = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var subscription = existing.FirstOrDefault(item =>
            string.Equals(item.Product?.Handle, plan.Handle, StringComparison.Ordinal) &&
            !string.Equals(item.State, "canceled", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.State, "expired", StringComparison.OrdinalIgnoreCase))
            ?? await _maxio.CreateSubscriptionAsync(customer.Id, user.Id, plan.Handle!, cancellationToken);

        return ToDto(subscription, plan);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(principal);
        var customer = await _maxio.FindCustomerAsync($"eshop-user-{user.Id}", cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => ToDto(subscription, null)).ToList();
    }

    private async Task<ApplicationUser> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new CurrentUserNotFoundException();
        }

        return await _userManager.FindByNameAsync(userName) ?? throw new CurrentUserNotFoundException();
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription, MaxioProduct? fallbackPlan) => new(
        subscription.Id,
        subscription.Product?.Handle ?? fallbackPlan?.Handle ?? string.Empty,
        subscription.Product?.Name ?? fallbackPlan?.Name ?? string.Empty,
        subscription.ProductPriceInCents ?? fallbackPlan?.PriceInCents ?? 0,
        subscription.State ?? string.Empty,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
}

public sealed class InvalidSubscriptionPlanException : Exception
{
    public InvalidSubscriptionPlanException() : base("The requested subscription plan is not available.") { }
}

public sealed class CurrentUserNotFoundException : Exception
{
    public CurrentUserNotFoundException() : base("The authenticated user no longer exists.") { }
}
