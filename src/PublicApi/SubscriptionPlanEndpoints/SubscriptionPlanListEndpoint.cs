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
/// Lists the subscription plans available to shoppers
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService subscriptionBillingService) =>
            {
                return await HandleAsync(subscriptionBillingService);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService subscriptionBillingService)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await subscriptionBillingService.ListPlansAsync();

        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }));

        return Results.Ok(response);
    }
}
