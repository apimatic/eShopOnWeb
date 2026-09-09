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
/// Lists the authenticated shopper's subscriptions, as reflected by the billing system of record (Maxio).
/// The shopper's identity is taken from the JWT.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(httpContext, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, ISubscriptionService subscriptionService)
    {
        var shopper = ShopperIdentity.FromPrincipal(httpContext.User);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsAsync(
                shopper.Reference, httpContext.RequestAborted);

            var response = new MySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(SubscriptionMappings.ToDto).ToList(),
            };

            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return Results.Problem(
                title: "Your subscriptions could not be retrieved from the billing system.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
