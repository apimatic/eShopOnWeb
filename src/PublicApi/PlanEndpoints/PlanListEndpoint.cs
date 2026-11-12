using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class PlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioBillingService billing, CancellationToken ct) =>
            {
                var response = new ListPlansResponse();
                response.Plans.AddRange(await billing.ListPlansAsync(ct));
                return Results.Ok(response);
            })
            .Produces<ListPlansResponse>()
            .WithTags("PlanEndpoints");
    }
}
