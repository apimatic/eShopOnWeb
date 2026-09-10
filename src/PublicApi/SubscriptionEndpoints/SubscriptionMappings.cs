using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps between the billing domain types and the API DTOs, and derives the billing identity from the
/// authenticated caller. The caller's identity comes entirely from the JWT — the Name claim, which in
/// this app is the user's email — and is used as the stable Maxio customer reference.
/// </summary>
internal static class SubscriptionMappings
{
    /// <summary>The authenticated user's stable reference (email, from the JWT Name claim), or null.</summary>
    public static string? GetUserReference(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    /// <summary>Builds the billing user from the caller's reference. Maxio requires a first/last name.</summary>
    public static BillingUser ToBillingUser(string reference)
    {
        var email = reference;
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email.Substring(0, atIndex) : email;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        return new BillingUser(reference, email, firstName, "eShopOnWeb");
    }

    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
    };

    public static SubscriptionDto ToDto(SubscriptionDetails subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        NextBillingDate = subscription.NextBillingDate,
        Reference = subscription.Reference,
    };
}
