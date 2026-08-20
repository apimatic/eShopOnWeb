using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class GetSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal principal,
                IShopperIdentityResolver identityResolver,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                if (await identityResolver.ResolveAsync(principal) is null)
                {
                    return Results.Unauthorized();
                }

                var plans = await billingService.GetPlansAsync(cancellationToken);
                return Results.Ok(new SubscriptionPlanListResponse(plans));
            })
            .Produces<SubscriptionPlanListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithName("GetSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }
}
