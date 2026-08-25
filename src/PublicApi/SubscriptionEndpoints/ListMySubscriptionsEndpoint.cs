using System.Linq;
using System.Security.Claims;
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
/// Lists the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionBillingService _billingService;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager, ISubscriptionBillingService billingService)
    {
        _userManager = userManager;
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriber = await SubscriberInfoFactory.CreateAsync(user, _userManager);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await _billingService.GetSubscriptionsAsync(subscriber.Reference);

        response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.Map));

        return Results.Ok(response);
    }
}
