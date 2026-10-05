using System.Linq;
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
/// Lists the subscriptions the authenticated user holds at the billing provider.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsListEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<MySubscriptionsListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        var user = await ResolveUserAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsListResponse();
        var subscriptions = await billingService.GetSubscriptionsForUserAsync(user.Id,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        response.Subscriptions.AddRange(subscriptions.Select(subscription => new SubscriptionDto
        {
            MaxioSubscriptionId = subscription.MaxioSubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt,
            CreatedAt = subscription.CreatedAt
        }));

        return Results.Ok(response);
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