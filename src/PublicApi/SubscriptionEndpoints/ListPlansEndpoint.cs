using System;
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
/// UC1 step 1 — lists the plans available to subscribe to. Anonymous (mirrors
/// <c>CatalogItemListPagedEndpoint</c> / <c>CatalogBrandListEndpoint</c>).
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
        var plans = await subscriptionService.ListPlansAsync();
        response.Plans.AddRange(plans.Select(p => p.ToDto()));
        return Results.Ok(response);
    }
}

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(Guid correlationId) : base(correlationId) { }

    public ListPlansResponse() { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
