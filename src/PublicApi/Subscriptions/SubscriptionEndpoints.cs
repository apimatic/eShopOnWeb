using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Read the configured Maxio subscription catalog and manage the caller's subscriptions.
/// </summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult, IMaxioSubscriptionService>
{
    // Route handlers below receive HttpContext so they can use the JWT caller and RequestAborted token.
    // The endpoint library requires this member for discovery; it is not a routable operation.
    public Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptions)
        => Task.FromResult<IResult>(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        var authorization = new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };

        app.MapGet("api/subscription-plans", async (IMaxioSubscriptionService subscriptions, HttpContext context) =>
            await ExecuteAsync(async () => Results.Ok(new SubscriptionPlansResponse(await subscriptions.GetPlansAsync(context.RequestAborted)))))
            .RequireAuthorization(authorization)
            .Produces<SubscriptionPlansResponse>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest? request, IMaxioSubscriptionService subscriptions, HttpContext context) =>
            await ExecuteAsync(async () =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.Problem("A planHandle is required.", statusCode: StatusCodes.Status400BadRequest);
                }

                var subscription = await subscriptions.SubscribeAsync(context.User, request.PlanHandle, context.RequestAborted);
                return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
            }))
            .RequireAuthorization(authorization)
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", async (IMaxioSubscriptionService subscriptions, HttpContext context) =>
            await ExecuteAsync(async () => Results.Ok(new MySubscriptionsResponse(await subscriptions.GetMySubscriptionsAsync(context.User, context.RequestAborted)))))
            .RequireAuthorization(authorization)
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (MaxioProviderException ex)
        {
            var statusCode = ex.StatusCode is >= 400 and < 500 ? ex.StatusCode : StatusCodes.Status502BadGateway;
            return Results.Problem(ex.Message, statusCode: statusCode);
        }
    }
}
