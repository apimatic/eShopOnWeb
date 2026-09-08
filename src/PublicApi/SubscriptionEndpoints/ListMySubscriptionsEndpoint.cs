using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// GET /api/my-subscriptions — lists the Maxio subscriptions of the authenticated shopper.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal principal,
                ISubscriptionService subscriptions,
                ILogger<ListMySubscriptionsEndpoint> logger,
                CancellationToken cancellationToken) =>
            {
                var email = SubscriptionEndpointSupport.GetUserEmail(principal);
                if (email is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var userSubscriptions = await subscriptions
                        .ListUserSubscriptionsAsync(email, cancellationToken)
                        .ConfigureAwait(false);

                    var response = new SubscriptionListResponse
                    {
                        Subscriptions = userSubscriptions
                            .Select(SubscriptionDto.FromMaxio)
                            .OrderByDescending(s => s.CreatedAt)
                            .ToList(),
                    };

                    return Results.Ok(response);
                }
                catch (Exception ex) when (ex is MaxioApiException or MaxioUnavailableException)
                {
                    return SubscriptionEndpointSupport.MapMaxioFailure(ex, logger);
                }
            })
            .Produces<SubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
