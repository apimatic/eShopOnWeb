using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// UC1 step 1: lists the recurring plans available in the configured Maxio product family. Anonymous -
/// mirrors <c>CatalogItemListPagedEndpoint</c>.
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, ListPlansRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListPlansRequest(), subscriptionService);
            })
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPlansRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListPlansResponse(request.CorrelationId());

        var plans = await subscriptionService.ListPlansAsync();
        foreach (var plan in plans)
        {
            response.Plans.Add(SubscriptionMapping.ToDto(plan));
        }

        return Results.Ok(response);
    }
}
