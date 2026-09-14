using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// Lists the authenticated shopper's subscriptions, as reported by the billing system. Returns an
/// empty list when the shopper has never subscribed.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var request = new MySubscriptionsRequest { Subscriber = user.ToSubscriberIdentity() };
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (request.Subscriber is null)
        {
            return Results.Problem("The authenticated token does not identify a user.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.GetSubscriptionsAsync(request.Subscriber, cancellationToken);
        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();

        return Results.Ok(response);
    }
}
