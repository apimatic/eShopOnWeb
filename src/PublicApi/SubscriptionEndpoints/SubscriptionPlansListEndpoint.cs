using System.Linq;
using System.Threading.Tasks;
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
/// Lists the subscription plans offered by the store (Maxio products of the configured family).
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlansListEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        var user = await ResolveUserAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var response = new SubscriptionPlanListResponse();
        var plans = await billingService.ListPlansAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? default);

        response.SubscriptionPlans.AddRange(plans.Select(plan => new SubscriptionPlanDto
        {
            MaxioProductId = plan.MaxioProductId,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            RequiresPaymentProfile = plan.RequiresPaymentProfile
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