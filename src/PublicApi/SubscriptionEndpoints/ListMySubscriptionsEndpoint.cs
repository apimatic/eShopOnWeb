using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.MaxioBilling;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, UserManager<ApplicationUser>, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (UserManager<ApplicationUser> userManager, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(userManager, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(UserManager<ApplicationUser> userManager, IMaxioBillingService billingService)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        var user = await CreateSubscriptionEndpoint.ResolveUserAsync(httpContext?.User ?? new System.Security.Claims.ClaimsPrincipal(), userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billingService.GetSubscriptionsForUserAsync(user, httpContext?.RequestAborted ?? CancellationToken.None);

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions);

        return Results.Ok(response);
    }
}
