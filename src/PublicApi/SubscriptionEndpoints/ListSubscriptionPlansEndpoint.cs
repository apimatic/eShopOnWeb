using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscribable plans in the configured Maxio product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, HttpContext http) =>
            {
                return await HandleAsync(billingService, http);
            })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService, HttpContext http)
    {
        try
        {
            var result = await billingService.GetPlansAsync(http.RequestAborted);
            return Results.Ok(new SubscriptionPlansResponse
            {
                Plans = result.Plans.Select(p => p.ToDto()).ToList(),
                Truncated = result.Truncated
            });
        }
        catch (SubscriptionBillingException ex)
        {
            return ex.ToResult();
        }
    }
}
