using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (MaxioSubscriptionService service, HttpContext context) =>
            {
                try
                {
                    return Results.Ok(await service.GetPlansAsync(context.RequestAborted));
                }
                catch (MaxioProviderException ex)
                {
                    return Failure(ex);
                }
            })
            .Produces<SubscriptionPlanDto[]>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscribeRequest request, MaxioSubscriptionService service, HttpContext context) =>
            {
                if (string.IsNullOrWhiteSpace(context.User.Identity?.Name))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscription = await service.SubscribeAsync(context.User.Identity.Name, request.PlanHandle, context.RequestAborted);
                    return Results.Created($"api/my-subscriptions/{subscription.Id}", subscription);
                }
                catch (MaxioProviderException ex)
                {
                    return Failure(ex);
                }
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (MaxioSubscriptionService service, HttpContext context) =>
            {
                if (string.IsNullOrWhiteSpace(context.User.Identity?.Name))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscriptions = await service.GetMySubscriptionsAsync(context.User.Identity.Name, context.RequestAborted);
                    return Results.Ok(new MySubscriptionsResponse(subscriptions));
                }
                catch (MaxioProviderException ex)
                {
                    return Failure(ex);
                }
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    private static IResult Failure(MaxioProviderException exception) => Results.Problem(
        statusCode: (int)exception.StatusCode,
        title: "Subscription billing request could not be completed",
        detail: exception.Message);
}
