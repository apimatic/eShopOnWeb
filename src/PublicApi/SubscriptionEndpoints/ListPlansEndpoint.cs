using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the recurring plans available to subscribe to (UC1, step 1). Anonymous — mirrors
/// the public catalog browsing endpoints.
/// </summary>
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
        var plans = await subscriptionService.GetAvailablePlansAsync();
        response.Plans.AddRange(plans.Select(PlanDto.FromBillingPlan));
        return Results.Ok(response);
    }
}

public class ListPlansResponse : BaseResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}
