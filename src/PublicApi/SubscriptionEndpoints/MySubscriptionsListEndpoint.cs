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

/// <summary>Lists the authenticated shopper's own subscriptions.</summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken cancellationToken) =>
            {
                var username = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(username))
                    return Results.Unauthorized();
                return await HandleAsync(username, billing, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    // Satisfies IEndpoint; the route handler calls the identity/cancellation-aware overload below.
    public Task<IResult> HandleAsync(IMaxioBillingService billing)
        => HandleAsync(string.Empty, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(string userReference, IMaxioBillingService billing, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await billing.GetSubscriptionsForUserAsync(userReference, cancellationToken);
        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();

        return Results.Ok(response);
    }
}
