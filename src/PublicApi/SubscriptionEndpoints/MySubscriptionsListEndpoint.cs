using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's own subscriptions. Returns an empty set when the shopper has
/// no Maxio customer yet (never creates anything). JWT-authenticated.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct)
                => await HandleAsync(user, billing, ct))
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    // Satisfies IEndpoint; the route lambda calls the identity/cancellation-aware overload.
    public Task<IResult> HandleAsync(IMaxioBillingService billing)
        => HandleAsync(principal: null, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal? principal, IMaxioBillingService billing,
        CancellationToken cancellationToken)
    {
        var user = BillingUserFactory.FromPrincipal(principal);
        if (user is null)
            return Results.Unauthorized();

        var subscriptions = await billing.ListSubscriptionsAsync(user, cancellationToken);

        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
        };

        return Results.Ok(response);
    }
}
