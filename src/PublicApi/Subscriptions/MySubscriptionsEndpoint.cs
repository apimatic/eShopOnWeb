using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionService _subscriptions;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(ISubscriptionService subscriptions, UserManager<ApplicationUser> userManager)
    {
        _subscriptions = subscriptions;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                var userName = httpContext.User.Identity?.Name;
                var user = string.IsNullOrWhiteSpace(userName) ? null : await _userManager.FindByNameAsync(userName);
                if (user == null)
                    return Results.Unauthorized();

                return Results.Ok(new MySubscriptionsResponse
                {
                    Subscriptions = new(await _subscriptions.ListMySubscriptionsAsync(user, cancellationToken))
                });
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() =>
        Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
}
