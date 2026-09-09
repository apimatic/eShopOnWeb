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
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's Maxio subscriptions.
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, Maxio.IMaxioSubscriptionService, UserManager<ApplicationUser>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, Maxio.IMaxioSubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager, CancellationToken ct) =>
            {
                return await GetSubscriptionsAsync(user, subscriptionService, userManager, ct);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(GetMySubscriptionsRequest request, Maxio.IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager) =>
        GetSubscriptionsAsync(new ClaimsPrincipal(), subscriptionService, userManager, CancellationToken.None);

    private async Task<IResult> GetSubscriptionsAsync(ClaimsPrincipal user,
        Maxio.IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, CancellationToken ct)
    {
        var request = new GetMySubscriptionsRequest();
        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        var username = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var appUser = await userManager.FindByNameAsync(username);
        if (appUser is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(appUser.Id, ct);
            response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();
            return Results.Ok(response);
        }
        catch (Maxio.MaxioBillingException ex)
        {
            return SubscriptionEndpointErrors.ToResult(ex);
        }
    }
}
