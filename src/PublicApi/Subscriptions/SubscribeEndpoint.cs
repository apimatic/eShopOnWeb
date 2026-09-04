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

public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly ISubscriptionService _subscriptions;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscribeEndpoint(ISubscriptionService subscriptions, UserManager<ApplicationUser> userManager)
    {
        _subscriptions = subscriptions;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (SubscribeRequest request, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                var userName = httpContext.User.Identity?.Name;
                var user = string.IsNullOrWhiteSpace(userName) ? null : await _userManager.FindByNameAsync(userName);
                if (user == null)
                    return Results.Unauthorized();

                var subscription = await _subscriptions.SubscribeAsync(user, request.PlanHandle, cancellationToken);
                return Results.Created("api/my-subscriptions", new SubscribeResponse { Subscription = subscription });
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request) =>
        Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
}
