using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionEndpoints : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (ISubscriptionService service, HttpContext context) =>
            {
                var plans = await service.ListPlansAsync(context.RequestAborted);
                return Results.Ok(new SubscriptionPlansResponse { SubscriptionPlans = [.. plans] });
            })
            .Produces<SubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateSubscriptionRequest request, ISubscriptionService service, HttpContext context) =>
            {
                var subscription = await service.SubscribeAsync(context.User, request.PlanHandle, context.RequestAborted);
                return Results.Ok(subscription);
            })
            .Produces<SubscriptionDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (ISubscriptionService service, HttpContext context) =>
            {
                var subscriptions = await service.ListMySubscriptionsAsync(context.User, context.RequestAborted);
                return Results.Ok(new MySubscriptionsResponse { Subscriptions = [.. subscriptions] });
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.NotFound());
}
