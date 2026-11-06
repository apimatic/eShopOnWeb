using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

public class ListPlansResponse
{
    public System.Collections.Generic.List<PlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (MaxioBillingService billing) => await HandleAsync(billing))
            .Produces<ListPlansResponse>()
            .WithTags("PlanEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioBillingService billing)
    {
        return Results.Ok(new ListPlansResponse { Plans = await billing.ListPlansAsync() });
    }
}
