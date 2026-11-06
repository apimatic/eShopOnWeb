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
public class SubscriptionCancelEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, System.Security.Claims.ClaimsPrincipal user, MaxioBillingService billing) =>
            {
                return await HandleAsync(subscriptionId, BillingEndpointHelpers.UserName(user), billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags(BillingEndpointHelpers.TAG);
    }

    public async Task<IResult> HandleAsync(int subscriptionId, string userName, MaxioBillingService billing)
    {
        return Results.Ok(await billing.CancelAsync(userName, subscriptionId));
    }
}
