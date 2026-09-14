using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class ListMySubscriptionsEndpoint : IEndpoint
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(
        ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var userName = context.User.Identity?.Name;
        var user = string.IsNullOrWhiteSpace(userName)
            ? null
            : await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(
            SubscriptionIdentity.From(user), cancellationToken);
        return Results.Ok(new MySubscriptionsResponse(
            subscriptions.Select(SubscriptionDto.From).ToList()));
    }
}
