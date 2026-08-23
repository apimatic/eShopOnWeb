using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (
                ISubscriptionBillingService billingService,
                HttpContext context) =>
            Results.Ok(await billingService.GetPlansAsync(context.RequestAborted)))
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<SubscriptionPlanDto[]>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
                CreateSubscriptionRequest request,
                IAuthenticatedBillingUser authenticatedUser,
                ISubscriptionBillingService billingService,
                HttpContext context) =>
            {
                var user = await authenticatedUser.GetAsync(context.RequestAborted);
                var response = await billingService.SubscribeAsync(
                    user,
                    request.ProductHandle,
                    context.RequestAborted);

                return response.Created
                    ? Results.Created("/api/my-subscriptions", response)
                    : Results.Ok(response);
            })
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("SubscriptionEndpoints");
    }
}

public sealed class MySubscriptionListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (
                IAuthenticatedBillingUser authenticatedUser,
                ISubscriptionBillingService billingService,
                HttpContext context) =>
            {
                var user = await authenticatedUser.GetAsync(context.RequestAborted);
                return Results.Ok(await billingService.GetSubscriptionsAsync(user, context.RequestAborted));
            })
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<SubscriptionDto[]>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}
