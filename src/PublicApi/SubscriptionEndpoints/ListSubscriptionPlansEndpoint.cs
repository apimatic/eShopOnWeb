using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans the signed-in shopper can subscribe to.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), billingService);
            })
            .RequireAuthorization()
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionBillingService billingService)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var plans = await billingService.ListPlansAsync();

        foreach (var plan in plans)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Handle = plan.Handle,
                Name = plan.Name,
                Description = plan.Description,
                PriceInCents = plan.PriceInCents,
                Price = plan.Price.ToString("0.00"),
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit,
                ProductFamilyHandle = plan.ProductFamilyHandle,
                PaymentMethodRequired = plan.PaymentMethodRequired
            });
        }

        return Results.Ok(response);
    }
}
