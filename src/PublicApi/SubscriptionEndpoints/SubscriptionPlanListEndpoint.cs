using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the recurring plans available for subscription (Maxio products of the
/// configured product family).
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionPlanListEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .RequireAuthorization()
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var plans = await subscriptionService.ListPlansAsync(CancellationToken.None);

        var response = new ListSubscriptionPlansResponse(Guid.NewGuid())
        {
            Plans = plans.Select(p => new SubscriptionPlanDto
            {
                MaxioProductId = p.MaxioProductId,
                ProductHandle = p.ProductHandle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Price = p.Price,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList()
        };

        return Results.Ok(response);
    }
}
