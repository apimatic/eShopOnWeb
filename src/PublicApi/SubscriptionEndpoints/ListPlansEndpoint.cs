using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans on offer (UC1 step 1). Anonymous, like the catalog endpoints.
/// </summary>
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

        try
        {
            var plans = await subscriptionService.GetPlansAsync();
            response.Plans.AddRange(plans.Select(SubscriptionPlanDto.From));
        }
        catch (Exception ex) when (SubscriptionEndpointResults.IsExpected(ex))
        {
            return SubscriptionEndpointResults.FromException(ex);
        }

        return Results.Ok(response);
    }
}
