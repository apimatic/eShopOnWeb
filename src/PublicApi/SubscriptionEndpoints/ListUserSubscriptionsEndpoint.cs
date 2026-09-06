using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class ListUserSubscriptionsEndpoint
{
    public static void MapListUserSubscriptionsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
           .Produces<SubscriptionDto[]>()
           .WithName("GetMySubscriptions")
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext,
        CancellationToken ct)
    {
        try
        {
            var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(username))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByNameAsync(username);
            if (user == null)
            {
                return Results.NotFound("User not found");
            }

            var subscriptions = await subscriptionService.ListUserSubscriptionsAsync(user.Id, ct);

            return Results.Ok(subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                PriceUSD = s.PriceUSD,
                State = s.State,
                NextBillingDate = s.NextBillingDate
            }).ToArray());
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
