using System.Linq;
using System.Security.Claims;
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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, UserManager<ApplicationUser>, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptions)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptions.GetMySubscriptionsAsync(user, _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        return Results.Ok(new MySubscriptionsResponse
        {
            Subscriptions = result.Select(SubscribeEndpoint.ToResponse).ToArray()
        });
    }
}
