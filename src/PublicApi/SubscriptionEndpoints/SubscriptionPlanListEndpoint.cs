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
/// Lists the available Maxio subscription plans for the configured product family
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
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService subscriptionBillingService)
    {
        var response = new SubscriptionPlanListResponse();

        var plans = await subscriptionBillingService.GetSubscriptionPlansAsync();
        response.Plans = plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Price = p.PriceInCents / 100m,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            RequiresPaymentMethod = p.RequiresPaymentMethod
        }).ToList();

        return Results.Ok(response);
    }
}
