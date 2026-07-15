using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists the available recurring plans (UC1 step 1). Anonymous — mirrors CatalogItemListPagedEndpoint.</summary>
public class ListPlansEndpoint : IEndpoint<IResult, ListPlansRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListPlansRequest(), subscriptionService);
            })
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPlansRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListPlansResponse(request.CorrelationId());

        try
        {
            var plans = await subscriptionService.ListPlansAsync();
            response.Plans.AddRange(plans.Select(p => new PlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                IntervalCount = p.IntervalCount,
                IntervalUnit = p.IntervalUnit,
                RequiresPaymentMethod = p.RequiresPaymentMethod
            }));
        }
        catch (BillingProviderException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Plans are temporarily unavailable");
        }

        return Results.Ok(response);
    }
}
