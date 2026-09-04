using System;
using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlansEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService service, HttpContext context) =>
                await HandleAsync(new GetSubscriptionPlansRequest(), service, context.RequestAborted))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, ISubscriptionService service, System.Threading.CancellationToken cancellationToken)
    {
        return Results.Ok(new SubscriptionPlansResponse
        {
            Plans = (await service.GetPlansAsync(cancellationToken)).ToList()
        });
    }

    public Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, ISubscriptionService service) =>
        HandleAsync(request, service, _httpContextAccessor.HttpContext?.RequestAborted ?? default);
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
            async (SubscribeRequest request, ISubscriptionService service, ClaimsPrincipal principal, HttpContext context) =>
                await HandleAsync(request, service, principal, context.RequestAborted))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService service, ClaimsPrincipal principal, System.Threading.CancellationToken cancellationToken)
    {
        return Results.Created("api/subscriptions", new SubscribeResponse
        {
            Subscription = await service.SubscribeAsync(principal, request.PlanHandle, cancellationToken)
        });
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService service)
    {
        var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HTTP request.");
        return HandleAsync(request, service, context.User, context.RequestAborted);
    }
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, ISubscriptionService>
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
            async (ISubscriptionService service, ClaimsPrincipal principal, HttpContext context) =>
                await HandleAsync(new GetMySubscriptionsRequest(), service, principal, context.RequestAborted))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, ISubscriptionService service, ClaimsPrincipal principal, System.Threading.CancellationToken cancellationToken)
    {
        return Results.Ok(new MySubscriptionsResponse
        {
            Subscriptions = (await service.GetMySubscriptionsAsync(principal, cancellationToken)).ToList()
        });
    }

    public Task<IResult> HandleAsync(GetMySubscriptionsRequest request, ISubscriptionService service)
    {
        var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HTTP request.");
        return HandleAsync(request, service, context.User, context.RequestAborted);
    }
}

public sealed class GetSubscriptionPlansRequest : BaseRequest;

public sealed class GetMySubscriptionsRequest : BaseRequest;
