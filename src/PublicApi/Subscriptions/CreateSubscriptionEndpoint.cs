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

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateSubscriptionRequest request,
                ClaimsPrincipal principal,
                AuthenticatedShopperResolver shopperResolver,
                ISubscriptionService subscriptionService,
                ILogger<CreateSubscriptionEndpoint> logger,
                CancellationToken cancellationToken) =>
            {
                var shopper = await shopperResolver.ResolveAsync(principal, cancellationToken);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }

                return await SubscriptionEndpointResults.ExecuteAsync(
                    () => subscriptionService.SubscribeAsync(shopper, request.ProductHandle, cancellationToken),
                    logger,
                    subscription => Results.Ok(subscription));
            })
            .Produces<SubscriptionDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }
}
