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
/// Lists the authenticated user's subscriptions (plan, price, state, next billing date)
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var userContext = await SubscriptionUserContextResolver.ResolveAsync(_httpContextAccessor, _userManager);
        if (userContext is null)
        {
            return Results.Unauthorized();
        }

        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var response = new ListMySubscriptionsResponse(request.CorrelationId());
        response.Subscriptions.AddRange(await subscriptionService.ListMySubscriptionsAsync(userContext.UserId, ct));
        return Results.Ok(response);
    }
}
