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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling user's subscriptions (GET api/my-subscriptions).
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, UserManager<ApplicationUser>>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(
        IMaxioSubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor)
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
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(UserManager<ApplicationUser> userManager)
    {
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var user = await SubscriptionEndpointHelpers.RequireUserAsync(userManager, _httpContextAccessor.HttpContext?.User);

        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(
            SubscriptionEndpointHelpers.BuildCustomerReference(user), ct);

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions);
        return Results.Ok(response);
    }
}
