using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared translation between the Maxio billing domain and the PublicApi DTOs,
/// including deriving Maxio customer attributes from the authenticated caller.
/// </summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductId = plan.ProductId
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.ProductHandle,
        PlanName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };

    /// <summary>The caller's username (from the JWT), used as the stable Maxio customer reference.</summary>
    public static string? GetUsername(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    /// <summary>
    /// Builds a <see cref="SubscribeCommand"/> for the authenticated caller. Identity is taken
    /// from the token (never client input); the username is used as both the email and the
    /// stable customer reference so the same eShopOnWeb user always maps to one Maxio customer.
    /// </summary>
    public static SubscribeCommand ToSubscribeCommand(this ClaimsPrincipal user, string planHandle)
    {
        var username = user.GetUsername()!;

        var atIndex = username.IndexOf('@');
        var firstName = atIndex > 0 ? username[..atIndex] : username;

        return new SubscribeCommand(
            CustomerReference: username,
            Email: username,
            FirstName: firstName,
            LastName: "eShopOnWeb",
            PlanHandle: planHandle);
    }
}
