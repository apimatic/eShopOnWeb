using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IBillingService billing, CancellationToken ct) => Results.Ok(await billing.ListPlansAsync(ct)))
            .Produces<IReadOnlyList<PlanDto>>()
            .WithTags("PlanEndpoints");
    }
}
