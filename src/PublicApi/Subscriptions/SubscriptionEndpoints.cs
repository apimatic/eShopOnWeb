using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (ISubscriptionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetPlansAsync(cancellationToken)))
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions", async (SubscribeRequest request, HttpContext context, ISubscriptionService service, CancellationToken cancellationToken) =>
            await SubscribeAsync(request, context, service, cancellationToken))
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions", async (HttpContext context, ISubscriptionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetMySubscriptionsAsync(context.User, cancellationToken)))
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> SubscribeAsync(
        SubscribeRequest request,
        HttpContext context,
        ISubscriptionService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest(new { error = "planHandle is required." });

        return Results.Ok(await service.SubscribeAsync(context.User, request.PlanHandle.Trim(), cancellationToken));
    }
}

public sealed class SubscribeRequest
{
    public string? PlanHandle { get; set; }
}
