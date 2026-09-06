using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (MaxioSubscriptionService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.ListPlansAsync(ct)))
            .RequireAuthorization()
            .Produces<SubscriptionPlanResponse>()
            .WithTags("Subscriptions");
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<SubscriptionPlanResponse>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (MaxioProviderException exception) { return ProviderProblem(exception); }
    }

    internal static IResult ProviderProblem(MaxioProviderException exception) => Results.Problem(
        statusCode: (int)(exception.StatusCode ?? HttpStatusCode.BadGateway),
        title: exception.Message);
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, ClaimsPrincipal user, MaxioSubscriptionService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(user.Identity?.Name))
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await service.SubscribeAsync(user.Identity.Name, request.PlanHandle, ct));
            }
            catch (MaxioProviderException exception)
            {
                return SubscriptionPlansEndpoint.ProviderProblem(exception);
            }
            catch (SubscriptionEnrollmentInProgressException exception)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: exception.Message);
            }
        })
        .RequireAuthorization()
        .Produces<SubscriptionDto>()
        .WithTags("Subscriptions");
    }
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal user, MaxioSubscriptionService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(user.Identity?.Name))
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await service.ListMySubscriptionsAsync(user.Identity.Name, ct));
            }
            catch (MaxioProviderException exception)
            {
                return SubscriptionPlansEndpoint.ProviderProblem(exception);
            }
        })
        .RequireAuthorization()
        .Produces<MySubscriptionsResponse>()
        .WithTags("Subscriptions");
    }
}
