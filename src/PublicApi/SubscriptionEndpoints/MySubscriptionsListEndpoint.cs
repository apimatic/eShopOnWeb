using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions (GET /api/my-subscriptions). Never creates a
/// Maxio customer; a user with no customer yet gets an empty list.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService subscriptionService,
                ISubscriptionIdentityResolver identityResolver,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, identityResolver, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        IMaxioSubscriptionService subscriptionService,
        ISubscriptionIdentityResolver identityResolver,
        CancellationToken cancellationToken)
    {
        var identity = await identityResolver.ResolveAsync();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(identity.UserName, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(SubscriptionRecordMapping.ToDto(subscription));
        }

        return Results.Ok(response);
    }
}
