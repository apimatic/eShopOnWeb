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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's subscriptions. Resolves the caller's Maxio customer by the
/// user reference (from the JWT); returns an empty list when the user has no Maxio customer yet.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billingService, CancellationToken cancellationToken) =>
            {
                var userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                var response = new ListMySubscriptionsResponse();
                var subscriptions = await billingService.GetSubscriptionsAsync(userName, cancellationToken);
                response.Subscriptions = subscriptions.Select(CustomerSubscriptionDto.FromModel).ToList();

                return Results.Ok(response);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    // Required by IEndpoint; the real handler lives in the route delegate because it needs the caller's identity.
    public Task<IResult> HandleAsync(IMaxioBillingService billingService) =>
        Task.FromResult(Results.BadRequest());
}
