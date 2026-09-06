using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Get available subscription plans
/// </summary>
public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
           .Produces<ListPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService subscriptionService)
    {
        var response = new ListPlansResponse();

        var plans = await subscriptionService.GetAvailablePlansAsync();
        response.Plans.AddRange(plans);

        return Results.Ok(response);
    }
}
