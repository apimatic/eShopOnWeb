using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class ListSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (ISubscriptionBillingService billing, CancellationToken ct) =>
                    Results.Ok(new SubscriptionPlansResponse(await billing.ListPlansAsync(ct))))
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }
}

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (CreateSubscriptionRequest request,
                    HttpContext context,
                    UserManager<ApplicationUser> userManager,
                    ISubscriptionBillingService billing,
                    CancellationToken ct) =>
                {
                    var user = await ResolveUserAsync(context, userManager);
                    if (user is null)
                    {
                        return Results.Unauthorized();
                    }
                    var subscription = await billing.SubscribeAsync(user, request.PlanHandle, ct);
                    return Results.Ok(subscription);
                })
            .Produces<SubscriptionDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<ApplicationUser?> ResolveUserAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager)
    {
        var username = context.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(username) ? null : await userManager.FindByNameAsync(username);
    }
}

public sealed class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (HttpContext context,
                    UserManager<ApplicationUser> userManager,
                    ISubscriptionBillingService billing,
                    CancellationToken ct) =>
                {
                    var username = context.User.Identity?.Name;
                    var user = string.IsNullOrWhiteSpace(username)
                        ? null
                        : await userManager.FindByNameAsync(username);
                    if (user is null)
                    {
                        return Results.Unauthorized();
                    }
                    return Results.Ok(new MySubscriptionsResponse(
                        await billing.ListMySubscriptionsAsync(user, ct)));
                })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .WithTags("SubscriptionEndpoints");
    }
}
