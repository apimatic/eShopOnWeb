using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Mapping between billing domain types and API DTOs, plus error → HTTP result mapping.</summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents is long cents ? cents / 100m : null,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
    };

    public static SubscriptionDto ToDto(this BillingSubscription s) => new()
    {
        SubscriptionId = s.Id,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        Price = s.PriceInCents is long cents ? cents / 100m : null,
        State = s.State,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextBillingDate = s.NextBillingDate,
        CreatedAt = s.CreatedAt,
    };

    /// <summary>
    /// Resolves the authenticated subscriber from the JWT. Returns null when the token carries no name
    /// claim (the endpoint then responds 401).
    /// </summary>
    public static SubscriberIdentity? GetSubscriber(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
                   ?? user.FindFirstValue(ClaimTypes.Name)
                   ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // In eShopOnWeb the user name is the user's email; use it for both identity and contact email.
        var email = user.FindFirstValue(ClaimTypes.Email) ?? name;
        return new SubscriberIdentity(name, email);
    }

    /// <summary>Maps a billing failure to a caller-safe HTTP result. Never leaks provider detail.</summary>
    public static IResult ToResult(this SubscriptionBillingException ex) => ex.Kind switch
    {
        BillingErrorKind.Validation => Results.BadRequest(new { error = ex.Message }),
        BillingErrorKind.NotFound => Results.NotFound(new { error = ex.Message }),
        BillingErrorKind.ProviderUnavailable => Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError),
    };
}
