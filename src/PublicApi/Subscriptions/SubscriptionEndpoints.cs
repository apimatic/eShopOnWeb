using System.Security.Claims;
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
    private readonly IMaxioBillingService _billingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlansEndpoint(IMaxioBillingService billingService, IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            () => HandleAsync())
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var plans = await _billingService.GetPlansAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        return Results.Ok(new SubscriptionPlansResponse(plans));
    }
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly IMaxioBillingService _billingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IMaxioBillingService billingService, IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (SubscribeRequest request) => HandleAsync(request))
            .Produces<SubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request)
    {
        var context = _httpContextAccessor.HttpContext!;
        var identity = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.FindFirstValue(ClaimTypes.Name);
        var subscription = await _billingService.SubscribeAsync(identity ?? string.Empty, request.PlanHandle, context.RequestAborted);
        return Results.Ok(new SubscriptionResponse(subscription));
    }
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioBillingService _billingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IMaxioBillingService billingService, IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            () => HandleAsync())
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var context = _httpContextAccessor.HttpContext!;
        var identity = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.FindFirstValue(ClaimTypes.Name);
        var subscriptions = await _billingService.GetMySubscriptionsAsync(identity ?? string.Empty, context.RequestAborted);
        return Results.Ok(new MySubscriptionsResponse(subscriptions));
    }
}
