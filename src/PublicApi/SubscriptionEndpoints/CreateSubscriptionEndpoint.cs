using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated shopper in a subscription plan. The caller's identity comes from the
/// JWT; the request body carries the plan handle. The operation is idempotent: repeated calls (e.g.
/// a double-click) will not create duplicate customers or subscriptions.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly SubscriberIdentityResolver _identityResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(SubscriberIdentityResolver identityResolver, IHttpContextAccessor httpContextAccessor)
    {
        _identityResolver = identityResolver;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
                await HandleAsync(request, subscriptionService))
            .Produces<SubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var subscriber = await _identityResolver.ResolveAsync(principal);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle ?? string.Empty, CancellationToken.None);

        var response = new SubscriptionResponse
        {
            Subscription = SubscriptionDto.FromDomain(subscription)
        };

        return Results.Ok(response);
    }
}

/// <summary>Request body for <see cref="CreateSubscriptionEndpoint"/>.</summary>
public class CreateSubscriptionRequest
{
    /// <summary>The handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string? PlanHandle { get; set; }
}

/// <summary>Response payload confirming the shopper's subscription.</summary>
public class SubscriptionResponse
{
    public SubscriptionDto Subscription { get; set; } = new();
}
