using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto(
            plan.Handle,
            plan.Name,
            plan.Description,
            plan.PriceInCents,
            plan.Interval,
            plan.IntervalUnit);
    }
}

public sealed record SubscriptionDto(
    int Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string Currency,
    string State,
    DateTimeOffset? NextBillingAt)
{
    public static SubscriptionDto From(SubscriptionDetails subscription)
    {
        return new SubscriptionDto(
            subscription.Id,
            subscription.PlanHandle,
            subscription.PlanName,
            subscription.PriceInCents,
            subscription.Currency,
            subscription.State,
            subscription.NextBillingAt);
    }
}

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

internal static class SubscriptionUserFactory
{
    public static SubscriptionUser Create(ClaimsPrincipal principal)
    {
        // eShop usernames are unique and remain stable when the in-memory Identity store is
        // recreated; the generated Identity database key does not.
        var userId = principal.Identity?.Name ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(email) ||
            !email.Contains('@'))
        {
            throw new UnauthorizedAccessException("The bearer token does not contain a usable user identity and email address.");
        }

        var givenName = principal.FindFirst(ClaimTypes.GivenName)?.Value;
        var surname = principal.FindFirst(ClaimTypes.Surname)?.Value;
        if (string.IsNullOrWhiteSpace(givenName))
        {
            var localPart = email.Split('@', 2)[0];
            givenName = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)[0];
        }

        return new SubscriptionUser(
            userId,
            email,
            Truncate(givenName, 100),
            Truncate(string.IsNullOrWhiteSpace(surname) ? "eShop Customer" : surname, 100));
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
