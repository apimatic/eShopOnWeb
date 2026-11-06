using System;
using System.Linq;
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
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }

    public Task<IResult> HandleAsync(BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            var response = new ListPlansResponse();
            var plans = await billing.ListPlansAsync();
            response.Plans.AddRange(plans.Select(BillingService.ToDto));
            return Results.Ok(response);
        });
    }
}
