using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent — a Maxio customer and a subscription
/// for the (user, plan) pair are ensured exactly once, so a double-click never creates duplicates.
/// The caller's identity is taken from the JWT; the request body only carries the chosen plan.
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SubscribeRequest? request,
                ClaimsPrincipal principal,
                ISubscriptionBillingService billing,
                UserManager<ApplicationUser> userManager,
                CancellationToken cancellationToken) =>
            {
                var user = await SubscriptionEndpointHelpers.ResolveBillingUserAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscription = await billing.SubscribeAsync(
                        user, request ?? new SubscribeRequest(), cancellationToken);
                    return Results.Ok(new SubscribeResponse { Subscription = subscription });
                }
                catch (SubscriptionBillingException ex)
                {
                    return SubscriptionEndpointHelpers.ToProblem(ex);
                }
            })
            .Produces<SubscribeResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}
