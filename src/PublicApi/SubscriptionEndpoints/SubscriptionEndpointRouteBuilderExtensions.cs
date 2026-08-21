using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SubscriptionBillingService service,
                CancellationToken cancellationToken) =>
                await ExecuteAsync(() => service.ListPlansAsync(cancellationToken)))
            .Produces<SubscriptionPlanDto[]>()
            .Produces<SubscriptionProblem>(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateSubscriptionRequest request,
                HttpContext httpContext,
                CurrentBillingUserFactory userFactory,
                SubscriptionBillingService service,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                {
                    return Results.BadRequest(new SubscriptionProblem
                    {
                        Code = "product_handle_required",
                        Message = "productHandle is required."
                    });
                }

                return await ExecuteAsync(async () =>
                {
                    var user = await userFactory.CreateAsync(httpContext.User, cancellationToken);
                    var result = await service.SubscribeAsync(user, request.ProductHandle, cancellationToken);
                    return result.Created
                        ? Results.Created("/api/my-subscriptions", result.Subscription)
                        : Results.Ok(result.Subscription);
                });
            })
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .Produces<SubscriptionProblem>(StatusCodes.Status409Conflict)
            .Produces<SubscriptionProblem>(StatusCodes.Status422UnprocessableEntity)
            .Produces<SubscriptionProblem>(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                HttpContext httpContext,
                CurrentBillingUserFactory userFactory,
                SubscriptionBillingService service,
                CancellationToken cancellationToken) =>
                await ExecuteAsync(async () =>
                {
                    var user = await userFactory.CreateAsync(httpContext.User, cancellationToken);
                    return await service.ListSubscriptionsAsync(user, cancellationToken);
                }))
            .Produces<SubscriptionDto[]>()
            .Produces<SubscriptionProblem>(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");

        return app;
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            var value = await action();
            return value is IResult result ? result : Results.Ok(value);
        }
        catch (SubscriptionBillingException ex)
        {
            return Results.Json(
                new SubscriptionProblem { Code = ex.Code, Message = ex.Message },
                statusCode: ex.StatusCode);
        }
    }
}
