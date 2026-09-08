using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products in the configured product family)
/// that the signed-in shopper can subscribe to.
/// </summary>
public class SubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                MaxioSubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, cancellationToken);
            })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        MaxioSubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var plans = await subscriptionService.ListAvailablePlansAsync(cancellationToken);

        var response = new SubscriptionPlansResponse
        {
            Plans = new System.Collections.Generic.List<SubscriptionPlanDto>(plans)
        };

        return Results.Ok(response);
    }
}
