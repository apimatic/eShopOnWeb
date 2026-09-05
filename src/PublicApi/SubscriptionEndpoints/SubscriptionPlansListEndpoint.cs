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
/// Lists the subscription plans a shopper can subscribe to.
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, IMaxioSubscriptionGateway>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionGateway gateway) =>
            {
                return await HandleAsync(gateway);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionGateway gateway)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await gateway.GetPlansAsync();
        response.Plans.AddRange(plans.Select(plan => new SubscriptionPlanDto
        {
            PlanHandle = plan.Handle,
            Name = plan.Name,
            Price = plan.PriceAmount,
            BillingIntervalCount = plan.BillingIntervalCount,
            BillingIntervalUnit = plan.BillingIntervalUnit
        }));

        return Results.Ok(response);
    }
}
