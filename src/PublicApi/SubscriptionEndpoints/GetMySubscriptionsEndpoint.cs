using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions as held by the billing system. JWT-authenticated;
/// only the token owner's subscriptions are returned.
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    // Satisfies the interface; the route lambda calls the cancellation-aware overload below.
    public Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService)
        => HandleAsync(user, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var userIdentity = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userIdentity))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billingService.GetSubscriptionsForUserAsync(userIdentity, cancellationToken);
            var response = new ListMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(SubscriptionMapping.ToDto).ToList()
            };
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionMapping.ToProblem(ex);
        }
    }
}
