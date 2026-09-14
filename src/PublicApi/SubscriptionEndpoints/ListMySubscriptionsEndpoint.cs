using System.Linq;
using System.Security.Claims;
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
/// Lists the authenticated shopper's own subscriptions, as reported by Maxio. Identity comes from the JWT.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISubscriptionBillingService billing) =>
                await HandleAsync(
                    new ListMySubscriptionsRequest { UserReference = user.Identity?.Name ?? string.Empty },
                    billing))
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billing)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
            return Results.Unauthorized();

        var response = new ListMySubscriptionsResponse(request.CorrelationId());
        var subscriptions = await billing.GetSubscriptionsAsync(request.UserReference);
        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();
        return Results.Ok(response);
    }
}
