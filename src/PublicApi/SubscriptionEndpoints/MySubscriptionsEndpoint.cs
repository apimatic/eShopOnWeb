using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.MaxioBilling.Exceptions;
using Microsoft.eShopWeb.MaxioBilling.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the calling user.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var subscriber = httpContext.User.ToSubscriber();
                if (subscriber is null)
                {
                    return BillingResults.MissingIdentity();
                }

                return await HandleAsync(new ListMySubscriptionsRequest(subscriber), billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        ListMySubscriptionsRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId())
        {
            CustomerReference = request.Subscriber.Reference
        };

        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(request.Subscriber, cancellationToken);

            response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));

            return Results.Ok(response);
        }
        catch (BillingException exception)
        {
            return BillingResults.Problem(exception);
        }
    }
}
