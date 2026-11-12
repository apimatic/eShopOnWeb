using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(user, billing, ct);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken ct)
    {
        var userName = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(await billing.GetMySubscriptionsAsync(userName, ct));
        return Results.Ok(response);
    }
}
