using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: subscribing twice to the
/// same plan never creates a second Maxio customer or subscription.
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionCreateEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionCreateRequest request, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var username = _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true
            ? _httpContextAccessor.HttpContext!.User.Identity!.Name
            : null;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        // UserManager is scoped (it owns the identity DbContext); endpoint classes are
        // resolved once at startup, so scoped services must be resolved per-request.
        var httpContext = _httpContextAccessor.HttpContext!;
        var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.SubscribeAsync(
            user.Id,
            username,
            string.IsNullOrWhiteSpace(user.Email) ? username : user.Email,
            request.PlanHandle,
            CancellationToken.None);

        return SubscriptionResultMapper.ToHttpResult(result, summary =>
        {
            var response = new SubscriptionCreateResponse(request.CorrelationId())
            {
                Subscription = MapSubscription(summary)
            };
            return summary.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        });
    }

    private static SubscriptionDto MapSubscription(SubscriptionSummary summary) => new()
    {
        SubscriptionId = summary.SubscriptionId,
        PlanHandle = summary.PlanHandle,
        PlanName = summary.PlanName,
        Price = summary.Price,
        Currency = summary.Currency,
        State = summary.State,
        NextBillingDate = summary.NextBillingDate,
        CurrentPeriodEnd = summary.CurrentPeriodEnd,
        CreatedAt = summary.CreatedAt,
        AlreadySubscribed = summary.AlreadySubscribed
    };
}
