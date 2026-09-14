using System;
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
/// Lists the subscribable plans in the configured Maxio product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), billingService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioBillingService billingService)
    {
        try
        {
            var plans = await billingService.ListPlansAsync();
            var response = new ListSubscriptionPlansResponse(request.CorrelationId())
            {
                SubscriptionPlans = plans.Select(p => new SubscriptionPlanDto
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    PriceInCents = p.PriceInCents,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit,
                }).ToList(),
            };
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return MaxioBillingHttpResults.From(ex);
        }
    }
}
