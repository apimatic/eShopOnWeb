using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class PlanListEndpoint : IEndpoint<IResult, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BillingService billing) =>
            {
                return await HandleAsync(billing);
            })
            .Produces<List<PlanDto>>()
            .WithTags("PlanEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingService billing)
    {
        return Results.Ok(await billing.ListPlansAsync());
    }
}
