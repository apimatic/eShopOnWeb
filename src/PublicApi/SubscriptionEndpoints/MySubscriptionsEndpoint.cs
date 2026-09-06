using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the calling shopper.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, Subscriber, ISubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ISubscriptionService subscriptionService,
                UserManager<ApplicationUser> userManager,
                HttpContext httpContext) =>
            {
                var subscriber = await SubscriberResolver.ResolveAsync(httpContext.User, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(subscriber, subscriptionService, httpContext.RequestAborted);
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                "Lists the caller's subscriptions",
                "Reads the authenticated account's subscriptions from the billing system of record. " +
                "An account that has never subscribed gets an empty list."));
    }

    public async Task<IResult> HandleAsync(
        Subscriber subscriber,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new MySubscriptionsResponse
        {
            CustomerReference = subscriber.CustomerReference
        };

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(subscriber, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapping.ToDto));
        response.CustomerId = subscriptions.Count > 0 ? subscriptions[0].CustomerId : null;

        return Results.Ok(response);
    }
}
