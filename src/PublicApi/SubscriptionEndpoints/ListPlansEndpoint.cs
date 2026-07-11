using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the recurring plans available for subscription (UC1 step 1). Anonymous - browsing plans does not
/// require an eShopOnWeb account.
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
        response.Plans = plans.Select(BillingPlanDto.FromDomain).ToList();

        return Results.Ok(response);
    }
}
