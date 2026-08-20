using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class GetMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal principal,
                IShopperIdentityResolver identityResolver,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                var shopper = await identityResolver.ResolveAsync(principal);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }

                var subscriptions = await billingService.GetSubscriptionsAsync(shopper, cancellationToken);
                return Results.Ok(new MySubscriptionsResponse(subscriptions));
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithName("GetMySubscriptions")
            .WithTags("SubscriptionEndpoints");
    }
}
