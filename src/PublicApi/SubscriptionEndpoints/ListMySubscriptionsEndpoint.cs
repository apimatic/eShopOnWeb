using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions of the authenticated shopper, as recorded by the billing system.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriberResolver, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriberResolver subscriberResolver,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriberResolver, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(
        ISubscriberResolver subscriberResolver,
        ISubscriptionBillingService billingService) =>
        HandleAsync(subscriberResolver, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        ISubscriberResolver subscriberResolver,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriber = await subscriberResolver.ResolveCurrentAsync();
        if (subscriber is null)
        {
            return SubscriptionResults.UnknownSubscriber();
        }

        var subscriptions = await billingService.GetSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapper.ToDto));

        return Results.Ok(response);
    }
}
