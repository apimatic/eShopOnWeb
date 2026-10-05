using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products) that shoppers can subscribe to.
/// Browsing plans is public, like the product catalog.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await subscriptionService.GetPlansAsync();

        response.SubscriptionPlans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Id = p.ProductId,
            Handle = p.Handle ?? string.Empty,
            Name = p.Name ?? string.Empty,
            Description = p.Description,
            Price = PriceFromCents(p.PriceInCents),
            PriceInCents = p.PriceInCents,
            BillingInterval = p.Interval,
            BillingIntervalUnit = p.IntervalUnit ?? string.Empty,
            RequiresPaymentMethod = p.RequireCreditCard,
            IsTaxable = p.Taxable
        }));

        return Results.Ok(response);
    }

    internal static decimal PriceFromCents(long priceInCents) => priceInCents / 100m;
}