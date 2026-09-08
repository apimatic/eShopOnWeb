using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a subscription plan. Idempotent:
/// subscribing twice to the same plan returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateSubscriptionRequest request,
                MaxioSubscriptionService subscriptionService,
                UserManager<ApplicationUser> userManager,
                ClaimsPrincipal principal,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, subscriptionService, userManager, principal, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.SubscribeAsync(user, request.PlanHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription,
            NewlyCreated = result.NewlyCreated
        };

        return Results.Ok(response);
    }

    internal static async Task<ApplicationUser?> ResolveUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return await userManager.FindByNameAsync(name);
    }
}
