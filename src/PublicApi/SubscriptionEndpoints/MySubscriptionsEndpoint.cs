using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions the authenticated user holds in the billing system.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleCoreAsync(new MySubscriptionsRequest(), user, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        return await HandleCoreAsync(request, new ClaimsPrincipal(), subscriptionService);
    }

    private async Task<IResult> HandleCoreAsync(MySubscriptionsRequest request, ClaimsPrincipal user,
        ISubscriptionService subscriptionService)
    {
        var resolution = await SubscriptionUserResolver.ResolveAsync(user, _userManager);
        if (resolution == null)
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.ListUserSubscriptionsAsync(resolution.Value.UserId);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapper.Map));

        return Results.Ok(response);
    }
}
