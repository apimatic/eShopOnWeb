using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the plans a shopper can subscribe to, sourced live from Maxio's product catalog.
/// </summary>
public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                return await HandleAsync(new GetSubscriptionPlansRequest(), maxioSubscriptionService);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, IMaxioSubscriptionService maxioSubscriptionService)
    {
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());

        var plans = await maxioSubscriptionService.GetAvailablePlansAsync();
        response.Plans = plans.Select(SubscriptionMapping.ToDto).ToList();

        return Results.Ok(response);
    }
}
