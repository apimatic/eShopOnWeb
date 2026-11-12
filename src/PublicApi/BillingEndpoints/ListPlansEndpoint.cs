using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            async (PlanCatalog plans, CancellationToken cancellationToken) =>
                Results.Ok(new ListPlansResponse { Plans = new List<PlanDto>(await plans.ListPlansAsync(cancellationToken)) }))
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }
}

public class ListPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}
