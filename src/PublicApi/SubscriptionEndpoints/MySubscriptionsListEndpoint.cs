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
/// Lists the authenticated eShopOnWeb user's Maxio subscriptions. Returns an empty list if
/// they have never subscribed (no Maxio customer exists for them yet).
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsListEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(user, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        var applicationUser = await _userManager.FindByNameAsync(user.Identity!.Name!);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billingService.GetSubscriptionsForCustomerAsync(applicationUser.Id);

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDtoMapper.ToDto));

        return Results.Ok(response);
    }
}
