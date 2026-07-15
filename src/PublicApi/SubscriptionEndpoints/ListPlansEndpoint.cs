using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC1 step 1: browse available subscription plans. Anonymous — mirrors CatalogItemListPagedEndpoint.</summary>
public class ListPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/plans",
            async (ISubscriptionService subscriptionService) => await HandleAsync(subscriptionService))
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var response = new ListPlansResponse();
        var plans = await subscriptionService.ListPlansAsync();
        response.Plans = plans.Select(SubscriptionPlanDto.FromDomain).ToList();
        return Results.Ok(response);
    }
}
