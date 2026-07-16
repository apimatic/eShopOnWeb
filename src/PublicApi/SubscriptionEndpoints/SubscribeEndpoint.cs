using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// UC1 (hero): enroll the authenticated user in a plan. Idempotent — a customer who already has an active
/// subscription in the family gets that subscription back rather than a second enrollment.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscribeEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/subscribe",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                var user = await _userManager.GetUserAsync(principal);
                request.CustomerReference = principal.Identity!.Name!;
                request.Email = user?.Email ?? request.CustomerReference;

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await subscriptionService.SubscribeAsync(
            request.CustomerReference, request.Email, firstName: string.Empty, lastName: string.Empty, request.PlanHandle);

        response.Subscription = subscription;

        return Results.Ok(response);
    }
}
