using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Authenticated subscription catalog and enrollment endpoints.</summary>
public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (MaxioSubscriptionService subscriptions, HttpContext context) =>
            Results.Ok(new SubscriptionPlansResponse(await subscriptions.ListPlansAsync(context.RequestAborted))))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", async (SubscribeRequest request, ClaimsPrincipal user, MaxioSubscriptionService subscriptions, HttpContext context) =>
        {
            var subscription = await subscriptions.SubscribeAsync(user.Identity?.Name ?? string.Empty, request.PlanHandle, context.RequestAborted);
            return Results.Created("api/my-subscriptions", subscription);
        })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal user, MaxioSubscriptionService subscriptions, HttpContext context) =>
            Results.Ok(new MySubscriptionsResponse(await subscriptions.ListMySubscriptionsAsync(
                user.Identity?.Name ?? string.Empty, context.RequestAborted))))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }
}
