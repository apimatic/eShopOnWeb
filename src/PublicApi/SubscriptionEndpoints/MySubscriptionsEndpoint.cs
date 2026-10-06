using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated shopper's subscriptions as recorded in Maxio.
/// </summary>
public partial class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly ISubscriberResolver _subscriberResolver;

    public MySubscriptionsEndpoint(ISubscriberResolver subscriberResolver)
    {
        _subscriberResolver = subscriberResolver;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<MySubscriptionsResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        var subscriber = await _subscriberResolver.ResolveAsync();
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse();

        var subscriptions = await billingService.GetUserSubscriptionsAsync(subscriber);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            Price = s.Price.ToString("0.00"),
            NextBillingAt = s.NextBillingAt,
            ActivatedAt = s.ActivatedAt,
            CreatedAt = s.CreatedAt,
            CancelAtEndOfPeriod = s.CancelAtEndOfPeriod
        }));

        return Results.Ok(response);
    }
}
