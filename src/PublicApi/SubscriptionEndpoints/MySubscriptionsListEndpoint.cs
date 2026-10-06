using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscriptions belonging to the authenticated shopper (from Maxio)
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(user, subscriptionService);
            })
            .Produces<MySubscriptionsListResponse>()
            .Produces(401)
            .Produces(502)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService)
    {
        var response = new MySubscriptionsListResponse();

        var email = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.Unauthorized();
        }

        var shopper = new ShopperIdentity(email);

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsForShopperAsync(shopper);
            response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToSubscriptionDto()));
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointErrors.Upstream(ex);
        }
        catch (MaxioConfigurationException ex)
        {
            return SubscriptionEndpointErrors.NotConfigured(ex);
        }

        return Results.Ok(response);
    }
}
