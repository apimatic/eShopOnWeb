using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(billing);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        var plans = await billing.ListPlansAsync(CancellationToken.None);
        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans.Select(plan => new SubscriptionPlanDto
            {
                Handle = plan.Handle,
                Name = plan.Name,
                Description = plan.Description,
                Price = plan.Price,
                PriceInCents = plan.PriceInCents,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit
            }).ToList()
        };

        return Results.Ok(response);
    }
}
