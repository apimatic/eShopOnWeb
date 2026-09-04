using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (ISubscriptionService service, CancellationToken cancellationToken) => await HandleAsync(service, cancellationToken))
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService service, CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlanListResponse();
        response.Plans.AddRange(await service.GetPlansAsync(cancellationToken));
        return Results.Ok(response);
    }

    public Task<IResult> HandleAsync(ISubscriptionService service) => HandleAsync(service, CancellationToken.None);
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (SubscribeRequest request, ISubscriptionService service, CancellationToken cancellationToken) =>
                    await HandleAsync(request, service, cancellationToken))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService service, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest(new { message = "planHandle is required." });

        var subscription = await service.SubscribeAsync(_httpContextAccessor.HttpContext!.User, request.PlanHandle, cancellationToken);
        return subscription is null
            ? Results.NotFound(new { message = "The requested subscription plan was not found." })
            : Results.Created("api/my-subscriptions", new SubscribeResponse { Subscription = subscription });
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService service) => HandleAsync(request, service, CancellationToken.None);
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (ISubscriptionService service, CancellationToken cancellationToken) =>
                    await HandleAsync(service, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService service, CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(await service.GetMySubscriptionsAsync(_httpContextAccessor.HttpContext!.User, cancellationToken));
        return Results.Ok(response);
    }

    public Task<IResult> HandleAsync(ISubscriptionService service) => HandleAsync(service, CancellationToken.None);
}
