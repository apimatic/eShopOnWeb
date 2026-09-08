using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// Lists the subscription plans available for signup (from the configured Maxio product family).
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await subscriptionService.GetAvailablePlansAsync();

        response.SubscriptionPlans.AddRange(plans.Select(plan => new SubscriptionPlanDto
        {
            ProductId = plan.ProductId,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            BillingPeriod = plan.Interval == 1 ? $"per {plan.IntervalUnit}" : $"per {plan.Interval} {plan.IntervalUnit}s",
        }));

        return Results.Ok(response);
    }
}
