using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family.
/// JWT-authenticated; the caller's identity comes from the token.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionFacade>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionFacade subscriptionFacade) =>
            {
                return await HandleAsync(subscriptionFacade);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionFacade subscriptionFacade)
    {
        var plans = await subscriptionFacade.ListPlansAsync();

        var response = new ListSubscriptionPlansResponse
        {
            ProductFamilyHandle = subscriptionFacade.ProductFamilyHandle,
            Plans = plans.Select(SubscriptionEndpointMapping.ToDto).ToList()
        };

        return Results.Ok(response);
    }
}
