using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

/// <summary>Lists the plans a shopper can subscribe to.</summary>
public class PlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (MaxioBillingService billing, HttpContext http) =>
                MaxioEndpointSupport.RunAsync(billing, http, ct => billing.ListPlansAsync(ct)))
            .Produces<PlanListResult>()
            .WithTags("MaxioEndpoints");
    }
}
