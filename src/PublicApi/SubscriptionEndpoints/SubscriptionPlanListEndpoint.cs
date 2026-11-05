using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to subscribe to
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriberContext context, CancellationToken cancellationToken) =>
            {
                var plans = await context.Billing.ListPlansAsync(cancellationToken);
                return Results.Ok(new ListSubscriptionPlansResponse
                {
                    Plans = plans.Select(p => p.ToDto()).ToList()
                });
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
