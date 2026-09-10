using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Optional plan handle to subscribe to. When omitted, the configured default plan
    /// (Maxio:DefaultPlanHandle) is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when the shopper was already subscribed and no new subscription was created.</summary>
    public bool AlreadyExisted { get; set; }

    public int CustomerId { get; set; }
}

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a single Maxio customer exists for the
/// user and is idempotent: a repeated call (e.g. a double-click) returns the existing subscription
/// rather than creating a duplicate.
/// </summary>
/// <remarks>
/// Scoped services are resolved per-request from <see cref="HttpContext.RequestServices"/> because
/// endpoints are singletons; capturing a scoped <c>UserManager</c> / DbContext in the constructor
/// would be shared unsafely across concurrent requests.
/// </remarks>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request) => await HandleAsync(request))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var services = httpContext.RequestServices;
        var billing = services.GetRequiredService<ISubscriptionBillingService>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var options = services.GetRequiredService<IOptions<MaxioOptions>>().Value;

        var subscriber = await SubscriberResolver.ResolveAsync(httpContext.User, userManager);

        // Plan comes from the request, falling back to the configured default. Never defaulted to a
        // hard-coded handle, so the same build works against a different catalog.
        var planHandle = !string.IsNullOrWhiteSpace(request.PlanHandle)
            ? request.PlanHandle!
            : options.DefaultPlanHandle ?? string.Empty;

        var result = await billing.SubscribeAsync(subscriber, planHandle, httpContext.RequestAborted);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(result.Subscription),
            AlreadyExisted = result.AlreadyExisted,
            CustomerId = result.CustomerId
        };

        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
