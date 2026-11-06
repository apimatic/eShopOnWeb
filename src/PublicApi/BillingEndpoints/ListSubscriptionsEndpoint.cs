using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Lists the caller's subscriptions
/// </summary>
public class ListSubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
            {
                return Results.Ok(await billing.ListSubscriptionsAsync(BillingEndpointHelpers.CallerName(user), null, ct));
            })
            .Produces<System.Collections.Generic.IReadOnlyList<SubscriptionDto>>()
            .WithTags("BillingEndpoints");
    }
}
