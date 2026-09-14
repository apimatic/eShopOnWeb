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
/// Lists the calling user's subscriptions, read live from Maxio (the system of record).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, CancellationToken>
{
    private readonly IMaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(IMaxioBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal callingUser, CancellationToken ct) =>
            {
                return await HandleAsync(callingUser, ct);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal callingUser, CancellationToken ct)
    {
        // The JWT this API issues carries only a Name claim (see IdentityTokenClaimService) --
        // no NameIdentifier -- so the calling user must be resolved by username, not GetUserAsync.
        var user = await _userManager.FindByNameAsync(callingUser.Identity?.Name ?? string.Empty);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await _billingService.ListMySubscriptionsAsync(user.Id, ct);

        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.MaxioSubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.Price,
            Currency = s.Currency,
            State = s.State,
            NextBillingDate = s.NextBillingDate
        }));

        return Results.Ok(response);
    }
}
