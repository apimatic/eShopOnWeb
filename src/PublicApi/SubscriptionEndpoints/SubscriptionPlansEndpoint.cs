using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans on offer (read from Maxio)
/// </summary>
public class SubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService service, CancellationToken ct) => await HandleAsync(service, ct))
           .Produces<SubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(ISubscriptionService service, CancellationToken ct)
    {
        var list = await service.ListPlansAsync(ct);
        var response = new SubscriptionPlansResponse { Truncated = list.Truncated };
        response.Plans.AddRange(list.Plans);
        return Results.Ok(response);
    }
}

public class SubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    /// <summary>True when the plan list may be incomplete (page cap hit).</summary>
    public bool Truncated { get; set; }
}
