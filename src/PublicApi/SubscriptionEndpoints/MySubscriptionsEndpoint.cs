using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions (plan, price, state, next billing date)
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioBillingService>
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
            (ClaimsPrincipal user, IMaxioBillingService billingService) =>
            {
                var shopper = await ShopperIdentityResolver.ResolveAsync(user, _userManager);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MySubscriptionsRequest { Shopper = shopper }, billingService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService billingService)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());
        var subscriptions = await billingService.ListMySubscriptionsAsync(request.Shopper!);
        response.Subscriptions.AddRange(subscriptions);
        return Results.Ok(response);
    }
}
