using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal principal,
                AuthenticatedShopperResolver shopperResolver,
                ISubscriptionService subscriptionService,
                ILogger<MySubscriptionListEndpoint> logger,
                CancellationToken cancellationToken) =>
            {
                var shopper = await shopperResolver.ResolveAsync(principal, cancellationToken);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }

                return await SubscriptionEndpointResults.ExecuteAsync(
                    () => subscriptionService.GetSubscriptionsAsync(shopper, cancellationToken),
                    logger,
                    subscriptions => Results.Ok(new MySubscriptionsResponse(subscriptions)));
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }
}
