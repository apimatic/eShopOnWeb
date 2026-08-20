using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed record SubscriptionPlanDto(long Id, string Handle, string Name, string Description,
    long PriceInCents, int Interval, string IntervalUnit)
{
    public static SubscriptionPlanDto From(BillingPlan plan) => new(plan.Id, plan.Handle, plan.Name,
        plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit);
}

public sealed class SubscribeRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionResponse
{
    public bool Created { get; init; }
    public required SubscriptionDto Subscription { get; init; }
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}

public sealed record SubscriptionDto(long Id, string ProductHandle, string ProductName, long PriceInCents,
    int Interval, string IntervalUnit, string State, DateTimeOffset? NextBillingDate)
{
    public static SubscriptionDto From(BillingSubscription subscription) => new(subscription.Id,
        subscription.ProductHandle, subscription.ProductName, subscription.PriceInCents, subscription.Interval,
        subscription.IntervalUnit, subscription.State, subscription.NextBillingAt);
}

internal static class BillingEndpointSupport
{
    public static async Task<BillingUser?> GetBillingUserAsync(ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username)) return null;

        var user = await userManager.FindByNameAsync(username);
        if (user is null) return null;

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email)) return null;

        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.FirstOrDefault() ?? localPart;
        var lastName = nameParts.Skip(1).FirstOrDefault() ?? "Customer";
        return new BillingUser(user.Id, email, firstName, lastName);
    }
}
