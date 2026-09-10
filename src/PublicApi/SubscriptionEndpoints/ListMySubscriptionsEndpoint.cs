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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the authenticated shopper. JWT-authenticated; the subscriber is
/// taken from the token. Returns an empty list when the user has no Maxio customer yet.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IMaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken) =>
                await HandleAsync(user, subscriptionService, cancellationToken))
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "List my subscriptions",
                description: "Lists the subscriptions held by the authenticated shopper."));
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var subscriber = user.ToSubscriber();
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await subscriptionService.GetSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();
        return Results.Ok(response);
    }
}
