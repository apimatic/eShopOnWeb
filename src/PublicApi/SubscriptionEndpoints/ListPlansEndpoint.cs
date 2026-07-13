using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family (UC1 step 1).
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var response = new ListPlansResponse();

        try
        {
            var plans = await subscriptionService.ListPlansAsync();
            response.Plans.AddRange(plans.Select(p => new PlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }));
        }
        catch (Microsoft.eShopWeb.ApplicationCore.Exceptions.BillingProviderException)
        {
            // Plans could not be listed (provider unreachable, bad credentials) -> friendly
            // empty result rather than a 502, matching UC1's "no enrollment is attempted" bar.
            return Results.Problem("The billing provider is currently unavailable. Please try again shortly.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(response);
    }
}
