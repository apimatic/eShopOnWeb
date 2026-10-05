using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions as recorded in Maxio Advanced Billing.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService, UserManager<ApplicationUser>>
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
            (IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(subscriptionService, userManager);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        var response = new ListMySubscriptionsResponse();

        var user = await CreateSubscriptionEndpoint.ResolveUserAsync(userManager,
            _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal());
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(user);

        response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.Map));

        return Results.Ok(response);
    }
}