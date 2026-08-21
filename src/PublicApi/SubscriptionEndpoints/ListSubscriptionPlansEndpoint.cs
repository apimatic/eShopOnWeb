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

public sealed class ListSubscriptionPlansEndpoint : IEndpoint
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListSubscriptionPlansEndpoint(
        ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<SubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(context);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        return Results.Ok(new SubscriptionPlansResponse(plans.Select(SubscriptionPlanDto.From).ToList()));
    }

    private Task<ApplicationUser?> GetUserAsync(HttpContext context)
    {
        var userName = context.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName)
            ? Task.FromResult<ApplicationUser?>(null)
            : _userManager.FindByNameAsync(userName);
    }
}
