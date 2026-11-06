using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (BillingService billing, CancellationToken ct) => await HandleAsync(billing, ct))
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingService billing, CancellationToken ct)
    {
        return Results.Ok(new ListPlansResponse { Plans = await billing.ListPlansAsync(ct) });
    }
}

public class ListPlansResponse
{
    public System.Collections.Generic.List<PlanDto> Plans { get; set; } = new();
}
