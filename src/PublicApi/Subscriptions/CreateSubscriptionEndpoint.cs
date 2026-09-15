using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
                SubscribeRequest request,
                UserManager<ApplicationUser> userManager,
                ISubscriptionService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                    return Results.BadRequest(new { message = "ProductHandle is required." });

                var userName = httpContext.User.FindFirstValue(ClaimTypes.Name);
                var user = string.IsNullOrWhiteSpace(userName)
                    ? null
                    : await userManager.FindByNameAsync(userName);
                if (user is null)
                    return Results.Unauthorized();

                var subscription = await service.SubscribeAsync(user, request.ProductHandle, cancellationToken);
                return Results.Created("api/my-subscriptions", new SubscribeResponse
                {
                    Subscription = subscription
                });
            })
            .RequireAuthorization(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build())
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }
}
