using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a subscription plan.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, Maxio.IMaxioSubscriptionService, UserManager<ApplicationUser>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, Maxio.IMaxioSubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager, CancellationToken ct) =>
            {
                return await SubscribeAsync(request, user, subscriptionService, userManager, ct);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, Maxio.IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager) =>
        SubscribeAsync(request, new ClaimsPrincipal(), subscriptionService, userManager, CancellationToken.None);

    private async Task<IResult> SubscribeAsync(SubscribeRequest request, ClaimsPrincipal user,
        Maxio.IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, CancellationToken ct)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { statusCode = 400, message = "planHandle is required." });
        }

        var username = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var appUser = await userManager.FindByNameAsync(username);
        if (appUser is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriber = new Maxio.MaxioSubscriber(appUser.Id, appUser.UserName ?? username, appUser.Email ?? username);
            var subscription = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle, ct);
            response.Subscription = subscription.ToDto();
            return Results.Created("api/my-subscriptions", response);
        }
        catch (Maxio.MaxioBillingException ex)
        {
            return SubscriptionEndpointErrors.ToResult(ex);
        }
    }
}
