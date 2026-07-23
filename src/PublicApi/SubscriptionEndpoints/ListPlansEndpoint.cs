using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the recurring subscription plans on offer (UC1, step 1).
/// </summary>
/// <remarks>Anonymous, so a plan catalogue can be shown before sign-in.</remarks>
public class ListPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) => await HandleAsync(subscriptionService))
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var response = new ListPlansResponse();

        var plans = await subscriptionService.ListPlansAsync();
        response.Plans.AddRange(plans.Select(plan => plan.ToDto()));

        return Results.Ok(response);
    }
}
