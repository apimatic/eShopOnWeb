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
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var subscriber = await SubscriberIdentityFactory.FromHttpContextAsync(httpContext, _userManager);
        if (subscriber == null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await _billingService.GetSubscriptionsAsync(subscriber);
        response.Subscriptions.AddRange(subscriptions.Select(ListMySubscriptionsResponse.ToDto));
        return Results.Ok(response);
    }
}
