using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    public const string Tag = "SubscriptionEndpoints";

    /// <summary>Resolves the eShop user for the authenticated caller. The JWT carries the username (= email).</summary>
    public static async Task<ApplicationUser?> ResolveCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }

    public static SubscriptionDto ToDto(CustomerSubscription s) => new()
    {
        Id = s.Id,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        Price = s.PriceInCents.HasValue ? s.PriceInCents.Value / 100m : null,
        State = s.State,
        NextBillingAt = s.NextBillingAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CreatedAt = s.CreatedAt
    };

    public static SubscriptionPlanDto ToDto(SubscriptionPlan p) => new()
    {
        Handle = p.Handle,
        Name = p.Name,
        Description = p.Description,
        PriceInCents = p.PriceInCents,
        Price = p.PriceInCents / 100m,
        IntervalCount = p.IntervalCount,
        IntervalUnit = p.IntervalUnit,
        ProductId = p.ProductId
    };

    /// <summary>Maps a billing failure to a coherent HTTP problem response with a caller-safe message.</summary>
    public static IResult ToProblem(SubscriptionBillingException ex) => ex.Kind switch
    {
        SubscriptionBillingErrorKind.InvalidRequest =>
            Results.Problem(title: "Invalid subscription request", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest),
        SubscriptionBillingErrorKind.ProviderUnavailable =>
            Results.Problem(title: "Billing provider unavailable", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway),
        _ =>
            Results.Problem(title: "Unexpected billing error", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway)
    };
}
