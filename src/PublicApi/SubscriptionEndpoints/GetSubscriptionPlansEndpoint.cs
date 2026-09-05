using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the sellable subscription plans in the configured Maxio product family.
/// </summary>
public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        var response = new SubscriptionPlansResponse();
        var plans = await billingService.GetPlansAsync();
        response.Plans.AddRange(plans.Select(SubscriptionMapper.ToDto));
        return Results.Ok(response);
    }
}
