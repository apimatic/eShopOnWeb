using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions that belong to the authenticated user.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService, IHttpContextAccessor>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (IMaxioSubscriptionService service, IHttpContextAccessor httpContextAccessor) =>
            {
                return await HandleAsync(service, httpContextAccessor);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        var username = httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Results.NotFound();
        }

        var subscriber = new MaxioSubscriber(user.Id, user.UserName ?? username, user.Email ?? username);
        var subscriptions = await service.ListMySubscriptionsAsync(subscriber);

        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(SubscriptionDtoMapper.ToSubscriptionDto).ToList()
        };
        return Results.Ok(response);
    }
}
