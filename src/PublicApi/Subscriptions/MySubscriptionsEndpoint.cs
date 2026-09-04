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

public sealed class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (
                UserManager<ApplicationUser> userManager,
                ISubscriptionService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var userName = httpContext.User.FindFirstValue(ClaimTypes.Name);
                var user = string.IsNullOrWhiteSpace(userName)
                    ? null
                    : await userManager.FindByNameAsync(userName);
                if (user is null)
                    return Results.Unauthorized();

                var response = new MySubscriptionsResponse();
                response.Subscriptions.AddRange(await service.GetMySubscriptionsAsync(user, cancellationToken));
                return Results.Ok(response);
            })
            .RequireAuthorization(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build())
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
