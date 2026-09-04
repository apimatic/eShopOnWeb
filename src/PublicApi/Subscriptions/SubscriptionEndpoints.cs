using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly SubscriptionService _service;

    public SubscriptionPlansEndpoint(SubscriptionService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (CancellationToken cancellationToken) =>
                {
                    return await HandleAsync(cancellationToken);
                })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var response = new SubscriptionPlansResponse(Guid.NewGuid());
        response.Plans.AddRange(await _service.GetPlansAsync(cancellationToken));
        return Results.Ok(response);
    }

    public Task<IResult> HandleAsync() => HandleAsync(CancellationToken.None);
}

public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly SubscriptionService _service;

    public SubscribeEndpoint(SubscriptionService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (SubscribeRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                {
                    return await HandleAsync(request, user, cancellationToken);
                })
            .Produces<SubscribeResponse>((int)HttpStatusCode.Created)
            .Produces((int)HttpStatusCode.BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = await _service.SubscribeAsync(user, request.PlanHandle.Trim(), cancellationToken)
        };
        return Results.Created($"api/subscriptions/{response.Subscription.Id}", response);
    }

    public Task<IResult> HandleAsync(SubscribeRequest request) =>
        Task.FromException<IResult>(new InvalidOperationException("The authenticated request context is required."));
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly SubscriptionService _service;

    public MySubscriptionsEndpoint(SubscriptionService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (ClaimsPrincipal user, CancellationToken cancellationToken) =>
                {
                    return await HandleAsync(user, cancellationToken);
                })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var response = new MySubscriptionsResponse(Guid.NewGuid());
        response.Subscriptions.AddRange(await _service.GetMySubscriptionsAsync(user, cancellationToken));
        return Results.Ok(response);
    }

    public Task<IResult> HandleAsync() =>
        Task.FromException<IResult>(new InvalidOperationException("The authenticated request context is required."));
}
