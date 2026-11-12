using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class ListPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billing, CancellationToken ct) =>
            {
                var plans = await billing.ListPlansAsync(ct);
                return Results.Ok(new ListPlansResponse { Plans = new List<PlanDto>(plans) });
            })
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }
}
