using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Returns the authenticated caller's subscriptions as reported by the billing system of record.
/// Empty if the caller has never been provisioned as a billing customer.
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService, ICurrentSubscriberAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, ICurrentSubscriberAccessor subscribers) =>
                await HandleAsync(billing, subscribers))
            .Produces<GetMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing, ICurrentSubscriberAccessor subscribers)
    {
        var response = new GetMySubscriptionsResponse();

        var subscriber = await subscribers.GetCurrentAsync();
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billing.GetSubscriptionsAsync(subscriber);
            response.Subscriptions = subscriptions.Select(SubscriptionDto.From).ToList();
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionResults.BillingError(ex);
        }
    }
}
