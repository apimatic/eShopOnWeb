using System;
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
/// Lists the subscription plans on offer
/// </summary>
public class SubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                var response = new ListSubscriptionPlansResponse(Guid.NewGuid());
                try
                {
                    var result = await subscriptions.ListPlansAsync(cancellationToken);
                    response.Plans.AddRange(result.Plans);
                    response.Truncated = result.Truncated;
                    return Results.Ok(response);
                }
                catch (SubscriptionException ex)
                {
                    return SubscriptionProblem.ToResult(ex);
                }
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
