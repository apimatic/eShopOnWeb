using System;
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

public static class CreateSubscriptionEndpoint
{
    public static void MapCreateSubscriptionEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
           .Produces<SubscriptionDto>()
           .WithName("CreateSubscription")
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
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

            var subscription = await subscriptionService.CreateOrUpdateSubscriptionAsync(
                userId: user.Id,
                userEmail: user.Email ?? string.Empty,
                userFirstName: user.UserName ?? "User",
                userLastName: string.Empty,
                planHandle: request.PlanHandle,
                ct: ct);

            return Results.Ok(new SubscriptionDto
            {
                Id = subscription.Id,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                PriceUSD = subscription.PriceUSD,
                State = subscription.State,
                NextBillingDate = subscription.NextBillingDate
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception)
        {
            return Results.StatusCode(500);
        }
    }
}

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = null!;
}
