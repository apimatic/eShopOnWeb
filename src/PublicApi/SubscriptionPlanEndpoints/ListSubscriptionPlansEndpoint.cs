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

/// <summary>
/// List Maxio subscription plans for the configured product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billing, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionPlanEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing)
        => HandleAsync(billing, CancellationToken.None);

    private async Task<IResult> HandleAsync(ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var plans = await billing.ListPlansAsync(cancellationToken);
        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Price = p.Price,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList()
        };

        return Results.Ok(response);
    }
}
