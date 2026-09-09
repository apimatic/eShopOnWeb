using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the authenticated user in the billing system.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService, ClaimsPrincipal>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, principal);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, ClaimsPrincipal principal)
    {
        var response = new MySubscriptionsResponse(Guid.NewGuid());

        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.ListSubscriptionsForUserAsync(user.Id, user.Email!, CancellationToken.None);
        response.Subscriptions.AddRange(subscriptions.Select(SubscribeEndpoint.ToDto));

        return Results.Ok(response);
    }
}
