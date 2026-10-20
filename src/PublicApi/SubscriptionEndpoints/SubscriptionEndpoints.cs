using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>GET api/subscription-plans: the plans the configured Maxio product family offers.</summary>
public class SubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionService service, ILogger<SubscriptionPlansEndpoint> logger, CancellationToken ct) =>
            {
                var response = new ListSubscriptionPlansResponse(Guid.NewGuid());
                using var scope = logger.BeginScope("CorrelationId {CorrelationId}", response.CorrelationId());
                try
                {
                    var listing = await service.ListPlansAsync(ct);
                    response.Plans.AddRange(listing.Plans);
                    response.IsComplete = listing.IsComplete;
                    return Results.Ok(response);
                }
                catch (SubscriptionFlowException ex)
                {
                    return SubscriptionEndpointSupport.Error(response.CorrelationId(), ex);
                }
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>POST api/subscriptions: subscribe the authenticated user to a plan (idempotent per user).</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
             SubscriptionService service, ILogger<SubscribeEndpoint> logger, CancellationToken ct) =>
            {
                var correlationId = request.CorrelationId();
                using var scope = logger.BeginScope("CorrelationId {CorrelationId}", correlationId);

                var user = await SubscriptionEndpointSupport.ResolveCallerAsync(principal, userManager);
                if (user is null) return Results.Unauthorized();

                try
                {
                    var view = await service.SubscribeAsync(new SubscribeCommand(user.Id, user.Email, user.UserName, request.PlanHandle), ct);
                    return Results.Ok(new SubscribeResponse(correlationId) { Subscription = view });
                }
                catch (SubscriptionFlowException ex)
                {
                    return SubscriptionEndpointSupport.Error(correlationId, ex);
                }
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>GET api/my-subscriptions: the authenticated user's own subscriptions with live plan/price/state/next billing date.</summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
             SubscriptionService service, ILogger<MySubscriptionsEndpoint> logger, CancellationToken ct) =>
            {
                var response = new MySubscriptionsResponse(Guid.NewGuid());
                using var scope = logger.BeginScope("CorrelationId {CorrelationId}", response.CorrelationId());

                var user = await SubscriptionEndpointSupport.ResolveCallerAsync(principal, userManager);
                if (user is null) return Results.Unauthorized();

                try
                {
                    response.Subscriptions.AddRange(await service.GetMySubscriptionsAsync(user.Id, ct));
                    return Results.Ok(response);
                }
                catch (SubscriptionFlowException ex)
                {
                    return SubscriptionEndpointSupport.Error(response.CorrelationId(), ex);
                }
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
