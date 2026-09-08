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
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Creates a new subscription to a subscription plan for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (
                CreateSubscriptionRequest request,
                ClaimsPrincipal principal,
                UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.BadRequest(new { Message = "planHandle is required." });
                }

                var user = await SubscriptionEndpointHelpers.FindCurrentUserAsync(principal, userManager);
                if (user is null)
                {
                    return Results.NotFound(new { Message = "The authenticated user could not be found." });
                }

                var result = await subscriptionService.SubscribeAsync(
                    user.Id,
                    user.Email ?? user.UserName ?? string.Empty,
                    request.PlanHandle,
                    request.FirstName,
                    request.LastName,
                    cancellationToken);

                var response = new CreateSubscriptionResponse
                {
                    IsNew = result.IsNew,
                    Subscription = result.Subscription
                };

                return result.IsNew
                    ? Results.Created("api/my-subscriptions", response)
                    : Results.Ok(response);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }
}
