using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the current user to a plan. Idempotent: re-subscribing to the same
/// plan returns the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, HttpContext httpContext, MaxioBillingService billingService) =>
            {
                return await HandleAsync(request, httpContext, billingService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, HttpContext httpContext, MaxioBillingService billingService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await billingService.SubscribeAsync(httpContext.User, request.ProductHandle, httpContext.RequestAborted);
        response.Subscription = subscription;

        return Results.Ok(response);
    }
}
