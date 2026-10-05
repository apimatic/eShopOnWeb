using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a subscription plan (idempotent per user and plan).
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, ISubscriptionBillingService>
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
            (SubscriptionCreateRequest request, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, ISubscriptionBillingService billingService)
    {
        var user = await ResolveUserAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var response = new SubscriptionCreateResponse(request.CorrelationId());

        var email = string.IsNullOrWhiteSpace(user.Email)
            ? $"{user.Id}@eshoponweb.invalid"
            : user.Email!;
        var subscription = await billingService.SubscribeAsync(user.Id, email, request.PlanHandle,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        response.Subscription = new SubscriptionDto
        {
            MaxioSubscriptionId = subscription.MaxioSubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt,
            CreatedAt = subscription.CreatedAt
        };

        return subscription.Existing
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }

    // Scoped services are resolved from the request scope: endpoint instances are created once
    // per route, so constructor-captured scoped services (UserManager, its DbContext) would be
    // shared across concurrent requests.
    private async Task<ApplicationUser?> ResolveUserAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var userName = httpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var userManager = httpContext!.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByNameAsync(userName);
    }
}