using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated shopper's subscriptions, as the billing system currently holds them
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, SubscriptionCaller, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionCaller caller, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(caller, billing);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCaller caller, ISubscriptionBillingService billing)
    {
        var response = new ListMySubscriptionsResponse();
        if (caller.Account is null)
        {
            return BillingResultHttpMapping.UnknownCaller(response.CorrelationId());
        }

        var result = await billing.ListSubscriptionsAsync(caller.Account, caller.RequestAborted);
        if (!result.IsSuccess)
        {
            return result.ToFailureResult(response.CorrelationId());
        }

        response.Subscriptions.AddRange(result.Value!.Select(SubscriptionDto.From));
        return Results.Ok(response);
    }
}
