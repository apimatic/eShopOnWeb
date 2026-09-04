using System.Security.Claims;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlansEndpoint(IMaxioSubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (
            IMaxioSubscriptionService service, CancellationToken cancellationToken) =>
        {
            var plans = await service.ListPlansAsync(cancellationToken);
            return Results.Ok(new SubscriptionPlansResponse { Plans = plans.ToList() });
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Produces<SubscriptionPlansResponse>()
        .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() =>
        HandleAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

    private async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var plans = await _service.ListPlansAsync(cancellationToken);
        return Results.Ok(new SubscriptionPlansResponse { Plans = plans.ToList() });
    }
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly IMaxioSubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
            SubscribeRequest request, ClaimsPrincipal principal, IMaxioSubscriptionService service,
            CancellationToken cancellationToken) =>
        {
            var subscription = await service.SubscribeAsync(principal, request.ProductHandle, cancellationToken);
            return Results.Ok(new SubscribeResponse { Subscription = subscription });
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Produces<SubscribeResponse>()
        .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request) =>
        HandleAsync(request, _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(),
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

    private async Task<IResult> HandleAsync(
        SubscribeRequest request, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var subscription = await _service.SubscribeAsync(principal, request.ProductHandle, cancellationToken);
        return Results.Ok(new SubscribeResponse { Subscription = subscription });
    }
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IMaxioSubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (
            ClaimsPrincipal principal, IMaxioSubscriptionService service, CancellationToken cancellationToken) =>
        {
            var subscriptions = await service.ListMySubscriptionsAsync(principal, cancellationToken);
            return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions.ToList() });
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Produces<MySubscriptionsResponse>()
        .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() =>
        HandleAsync(_httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(),
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

    private async Task<IResult> HandleAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var subscriptions = await _service.ListMySubscriptionsAsync(principal, cancellationToken);
        return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions.ToList() });
    }
}
