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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                async (SubscriptionBillingService service,
                       UserManager<ApplicationUser> userManager,
                       HttpContext httpContext,
                       CancellationToken cancellationToken) =>
                {
                    var userName = httpContext.User.Identity?.Name;
                    var user = string.IsNullOrWhiteSpace(userName)
                        ? null
                        : await userManager.FindByNameAsync(userName);
                    if (user is null)
                        return Results.Unauthorized();

                    return Results.Ok(new MySubscriptionsResponse
                    {
                        Subscriptions = await service.ListMySubscriptionsAsync(user, cancellationToken)
                    });
                })
            .Produces<MySubscriptionsResponse>()
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionBillingService service) =>
        Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status501NotImplemented));
}
