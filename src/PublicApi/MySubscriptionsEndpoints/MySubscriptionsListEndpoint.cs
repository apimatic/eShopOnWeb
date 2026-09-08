using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.MySubscriptionsEndpoints;

/// <summary>
/// Lists the signed-in user's billing subscriptions
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, UserManager<ApplicationUser>>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsListEndpoint(IMaxioSubscriptionService subscriptionService, IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(userManager);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("MySubscriptionsEndpoints");
    }

    public async Task<IResult> HandleAsync(UserManager<ApplicationUser> userManager)
    {
        var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(userName, cancellationToken);

        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.ToList()
        };

        return Results.Ok(response);
    }
}
