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
/// Lists Maxio subscriptions for the authenticated shopper.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(
        ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var user = await SubscriptionEndpointUser.ResolveAsync(_userManager, httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await _billingService.ListSubscriptionsForBuyerAsync(user.Id);
        return Results.Ok(ListMySubscriptionsResponse.From(subscriptions, System.Guid.NewGuid()));
    }
}
