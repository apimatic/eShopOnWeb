using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps between the provider-agnostic domain subscription types and their API DTOs, and derives
/// the billing customer identity from the authenticated caller's token.
/// </summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextBillingDate,
        Reference = subscription.Reference
    };

    /// <summary>
    /// Builds a <see cref="SubscribeRequest"/> from the caller's token identity. The user name
    /// claim (an email in eShopOnWeb) is the stable per-user reference used as the billing
    /// customer's idempotency key.
    /// </summary>
    public static SubscribeRequest ToSubscribeRequest(this ClaimsPrincipal user, string? planHandle)
    {
        var userName = user.GetUserReference();
        var atIndex = userName.IndexOf('@');
        var firstName = atIndex > 0 ? userName[..atIndex] : userName;
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "eShopOnWeb";
        }

        return new SubscribeRequest(
            UserReference: userName,
            Email: userName,
            FirstName: firstName,
            LastName: "eShopOnWeb Subscriber",
            PlanHandle: planHandle);
    }

    /// <summary>
    /// The stable per-user reference (the token's name claim) used as the billing customer key.
    /// </summary>
    public static string GetUserReference(this ClaimsPrincipal user) => user.Identity?.Name ?? string.Empty;
}
