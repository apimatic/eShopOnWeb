using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

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
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        HandleAsync(billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        try
        {
            var page = await billing.ListPlansAsync(cancellationToken);
            var response = new ListSubscriptionPlansResponse
            {
                Truncated = page.Truncated,
                Plans = page.Plans.Select(plan => new SubscriptionPlanDto
                {
                    Handle = plan.Handle,
                    Name = plan.Name,
                    Description = plan.Description,
                    PriceInCents = plan.PriceInCents,
                    Interval = plan.Interval,
                    IntervalUnit = plan.IntervalUnit
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionResults.FromBilling(ex);
        }
    }
}
