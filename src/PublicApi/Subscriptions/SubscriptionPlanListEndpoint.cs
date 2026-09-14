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

public sealed class SubscriptionPlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal principal,
                AuthenticatedShopperResolver shopperResolver,
                ISubscriptionService subscriptionService,
                ILogger<SubscriptionPlanListEndpoint> logger,
                CancellationToken cancellationToken) =>
            {
                if (await shopperResolver.ResolveAsync(principal, cancellationToken) is null)
                {
                    return Results.Unauthorized();
                }

                return await SubscriptionEndpointResults.ExecuteAsync(
                    () => subscriptionService.GetPlansAsync(cancellationToken),
                    logger,
                    plans => Results.Ok(new SubscriptionPlansResponse(plans)));
            })
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }
}
