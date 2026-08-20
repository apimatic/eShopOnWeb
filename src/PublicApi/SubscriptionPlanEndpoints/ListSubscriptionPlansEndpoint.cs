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
/// Lists recurring subscription plans from Maxio Advanced Billing.
/// </summary>
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
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        var plans = await billing.ListPlansAsync();
        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList()
        };

        return Results.Ok(response);
    }
}
