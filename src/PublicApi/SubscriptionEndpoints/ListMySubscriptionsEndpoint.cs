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
/// List the authenticated shopper's Maxio Advanced Billing subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser> userManager)
    {
        _billing = billing;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http) =>
            {
                return await HandleAsync(http);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var shopper = await SubscriptionEndpointHelpers.ResolveShopperAsync(http, _userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var result = await _billing.ListMySubscriptionsAsync(shopper, http.RequestAborted);
        return SubscriptionEndpointHelpers.MapResult(result, subscriptions =>
        {
            var response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));
            return Results.Ok(response);
        });
    }
}
