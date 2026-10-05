using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the JWT-authenticated user in the
/// billing system of record.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService,
    UserManager<ApplicationUser>>
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
            (ISubscriptionBillingService subscriptionBillingService, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(subscriptionBillingService, userManager);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService subscriptionBillingService,
        UserManager<ApplicationUser> userManager)
    {
        var response = new ListMySubscriptionsResponse(Guid.NewGuid());

        var httpUser = _httpContextAccessor.HttpContext?.User;
        if (httpUser is null)
        {
            return Results.Unauthorized();
        }

        var user = await SubscriptionUserResolver.ResolveAsync(httpUser, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionBillingService.GetSubscriptionsForUserAsync(
            user.Id, user.Email ?? user.UserName ?? string.Empty,
            user.UserName ?? user.Email ?? string.Empty);

        response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.MapSubscription));

        return Results.Ok(response);
    }
}