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

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, MaxioGateway>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioGateway billing) =>
            {
                return await HandleAsync(billing);
            })
            .Produces<PlanDto[]>()
            .WithTags("PlanEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioGateway billing)
    {
        return Results.Ok(await billing.ListPlansAsync());
    }
}
