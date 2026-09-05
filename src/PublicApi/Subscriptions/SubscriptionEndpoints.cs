using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    public Task<IResult> HandleAsync(SubscriptionBillingService billing) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status405MethodNotAllowed));

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (SubscriptionBillingService billing, CancellationToken cancellationToken) =>
            await SubscriptionEndpointResults.ExecuteAsync(async () => Results.Ok(await billing.GetPlansAsync(cancellationToken))))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<IReadOnlyList<SubscriptionPlanDto>>()
            .WithTags("Subscriptions");
    }
}

public sealed class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    public Task<IResult> HandleAsync(SubscriptionBillingService billing) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status405MethodNotAllowed));

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (SubscribeRequest request, HttpContext context,
            SubscriptionBillingService billing, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PlanHandle))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.PlanHandle)] = new[] { "PlanHandle is required." }
                });
            }

            return await SubscriptionEndpointResults.ExecuteAsync(async () =>
                Results.Ok(await billing.SubscribeAsync(context.User, request.PlanHandle, cancellationToken)));
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Produces<SubscriptionDto>()
        .ProducesValidationProblem()
        .WithTags("Subscriptions");
    }
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    public Task<IResult> HandleAsync(SubscriptionBillingService billing) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status405MethodNotAllowed));

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (HttpContext context, SubscriptionBillingService billing,
            CancellationToken cancellationToken) =>
            await SubscriptionEndpointResults.ExecuteAsync(async () =>
                Results.Ok(new MySubscriptionsResponse(await billing.GetMySubscriptionsAsync(context.User, cancellationToken)))))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }
}

internal static class SubscriptionEndpointResults
{
    public static async Task<IResult> ExecuteAsync(System.Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SubscriptionBillingException error)
        {
            return Results.Problem(statusCode: error.StatusCode, title: error.PublicMessage);
        }
    }
}
