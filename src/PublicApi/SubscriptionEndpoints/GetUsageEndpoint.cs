using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shows the metered usage of one of the caller's subscriptions on a single day
/// </summary>
public class GetUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, CancellationToken ct, BillingService billing) =>
            {
                return Results.Ok(await billing.GetUsageAsync(Caller.Name(user), subscriptionId, date, ct));
            })
            .Produces<UsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
