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
/// Shows one of the caller's subscriptions
/// </summary>
public class SubscriptionGetEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                return await HandleAsync(subscriptionId, user, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            var subscription = await billing.GetOwnedSubscriptionAsync(subscriptionId, user);
            return Results.Ok(BillingService.ToDto(subscription));
        });
    }
}
