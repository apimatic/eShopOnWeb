using System;
using System.Security.Claims;
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
/// Cancels one of the signed-in shopper's subscriptions
/// </summary>
public class CancelSubscriptionEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, MaxioGateway>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioGateway billing) =>
            {
                return await HandleAsync(subscriptionId, user, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, MaxioGateway billing)
    {
        return Results.Ok(await billing.CancelAsync(user.Identity!.Name!, subscriptionId));
    }
}
