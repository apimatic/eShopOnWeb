using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// List Maxio subscriptions for the authenticated shopper.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billing, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billing, user, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing)
        => HandleAsync(billing, new ClaimsPrincipal(), CancellationToken.None);

    private async Task<IResult> HandleAsync(
        ISubscriptionBillingService billing,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var shopper = await _userManager.FindByNameAsync(userName);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billing.ListMySubscriptionsAsync(shopper.Id, cancellationToken);
        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(SubscriptionMapper.ToDto).ToList()
        };

        return Results.Ok(response);
    }
}
