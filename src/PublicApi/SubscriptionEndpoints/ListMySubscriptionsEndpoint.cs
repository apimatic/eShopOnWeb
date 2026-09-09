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
/// Lists the authenticated caller's subscriptions. JWT-authenticated; the caller's identity comes
/// from the token. Returns an empty list when the caller has no billing customer yet.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, CancellationToken>
{
    private readonly ISubscriptionBillingService _billingService;

    public ListMySubscriptionsEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, HttpContext http) => await HandleAsync(user, http.RequestAborted))
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var identity = SubscriptionMappings.ToBillingIdentity(user);
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await _billingService.GetSubscriptionsForUserAsync(identity, cancellationToken);
        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(SubscriptionMappings.ToDto).ToList()
        };
        return Results.Ok(response);
    }
}
