using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// GET /api/my-subscriptions — lists the authenticated shopper's subscriptions, read back from Maxio
/// (the system of record).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(httpContext, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, IMaxioBillingService billingService)
    {
        if (!SubscriptionEndpointHelpers.TryGetSubscriber(httpContext.User, out var subscriber))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(subscriber, httpContext.RequestAborted);
            response.Subscriptions = subscriptions.Select(SubscriptionDto.From).ToList();
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointHelpers.ToErrorResult(ex);
        }

        return Results.Ok(response);
    }
}
