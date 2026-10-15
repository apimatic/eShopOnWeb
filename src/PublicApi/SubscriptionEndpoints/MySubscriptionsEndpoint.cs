using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions of the authenticated shopper
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                    return Results.Unauthorized();

                var response = new ListMySubscriptionsResponse(Guid.NewGuid());
                try
                {
                    var result = await subscriptions.ListMySubscriptionsAsync(userName, cancellationToken);
                    response.Subscriptions.AddRange(result.Subscriptions);
                    response.Truncated = result.Truncated;
                    return Results.Ok(response);
                }
                catch (SubscriptionException ex)
                {
                    return SubscriptionProblem.ToResult(ex);
                }
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
