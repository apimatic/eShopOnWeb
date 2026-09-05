using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>JWT-authenticated subscription plan discovery and enrollment.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioSubscriptionService subscriptions, HttpContext context) =>
            Results.Ok(new SubscriptionPlanResponse
            {
                Plans = (await subscriptions.GetPlansAsync(context.RequestAborted)).ToList()
            }))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlanResponse>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", async (SubscribeRequest request, IMaxioSubscriptionService subscriptions, HttpContext context) =>
            Results.Ok(await subscriptions.SubscribeAsync(CurrentUsername(context), request.ProductHandle, context.RequestAborted)))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionDto>()
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", async (IMaxioSubscriptionService subscriptions, HttpContext context) =>
            Results.Ok(new MySubscriptionsResponse
            {
                Subscriptions = (await subscriptions.GetMySubscriptionsAsync(CurrentUsername(context), context.RequestAborted)).ToList()
            }))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    private static string CurrentUsername(HttpContext context)
        => context.User.Identity?.Name ?? throw new SubscriptionApiException(StatusCodes.Status401Unauthorized, "Authentication is required.");

    // Routes are registered in AddRoute; the MinimalApi.Endpoint scanner also requires this member.
    public Task<IResult> HandleAsync() => Task.FromResult(Results.NotFound());
}
