using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan p) => new()
    {
        Handle = p.Handle,
        Name = p.Name,
        Description = p.Description,
        Price = p.Price,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit,
        RequiresPaymentMethod = p.RequiresPaymentMethod,
    };

    public static SubscriptionDto ToDto(this UserSubscription s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.Price,
        State = s.State,
        NextBillingDate = s.NextBillingDate,
    };

    /// <summary>The caller identity comes from the JWT (the token carries the user name, which is the account e-mail).</summary>
    public static string? BillingIdentity(this ClaimsPrincipal user) =>
        (user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name)?.Trim().ToLowerInvariant();
}
