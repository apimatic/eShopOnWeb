using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans a logged-in shopper can subscribe to.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioBillingService _maxioBillingService;

    public SubscriptionPlanListEndpoint(IMaxioBillingService maxioBillingService)
    {
        _maxioBillingService = maxioBillingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
           .Produces<SubscriptionPlanListResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new SubscriptionPlanListResponse();

        var plans = await _maxioBillingService.GetPlansAsync();
        response.SubscriptionPlans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = p.PriceInCents / 100m,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }));

        return Results.Ok(response);
    }
}
