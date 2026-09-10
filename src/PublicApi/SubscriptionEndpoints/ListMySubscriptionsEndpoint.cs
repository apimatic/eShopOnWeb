using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions (GET /api/my-subscriptions). The caller's identity is
/// taken from the JWT; a user with no billing customer yet simply has an empty list.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var reference = SubscriptionMappings.GetUserReference(user);
                if (string.IsNullOrWhiteSpace(reference))
                    return Results.Unauthorized();

                var subscriptions = await billing.ListSubscriptionsAsync(reference, cancellationToken);
                var response = new ListMySubscriptionsResponse();
                response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMappings.ToDto));
                return Results.Ok(response);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
