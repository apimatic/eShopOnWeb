using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// Lists the subscription plans available for signup.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
           .RequireAuthorization()
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await billingService.ListPlansAsync();

        response.Plans.AddRange(plans.Select(plan => new SubscriptionPlanDto
        {
            ProductId = plan.ProductId,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit
        }));

        return Results.Ok(response);
    }
}
