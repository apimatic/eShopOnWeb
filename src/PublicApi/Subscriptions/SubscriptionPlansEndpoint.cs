using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CancellationToken cancellationToken) => await HandleAsync(cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        return Results.Ok(new SubscriptionPlansResponse
        {
            Plans = await _subscriptionService.GetPlansAsync(cancellationToken)
        });
    }

    Task<IResult> IEndpoint<IResult>.HandleAsync() => HandleAsync(CancellationToken.None);
}

public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(
        UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, CancellationToken cancellationToken) =>
                await HandleAsync(request, cancellationToken))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var username = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await _subscriptionService.SubscribeAsync(user, request.ProductHandle, cancellationToken);
        return Results.Created("api/my-subscriptions", new SubscribeResponse
        {
            Subscription = subscription
        });
    }

    Task<IResult> IEndpoint<IResult, SubscribeRequest>.HandleAsync(SubscribeRequest request) =>
        HandleAsync(request, CancellationToken.None);
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(
        UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CancellationToken cancellationToken) => await HandleAsync(cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        var username = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new MySubscriptionsResponse
        {
            Subscriptions = await _subscriptionService.GetMySubscriptionsAsync(user, cancellationToken)
        });
    }

    Task<IResult> IEndpoint<IResult>.HandleAsync() => HandleAsync(CancellationToken.None);
}

public sealed class SubscribeRequest : BaseMessage
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = new List<SubscriptionPlanDto>();
}

public sealed class SubscribeResponse : BaseResponse
{
    public SubscriptionDto Subscription { get; init; } = new();
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = new List<SubscriptionDto>();
}
