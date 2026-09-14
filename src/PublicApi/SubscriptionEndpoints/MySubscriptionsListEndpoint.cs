using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions of the authenticated caller as recorded by Maxio Advanced Billing.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleCoreAsync(user, subscriptionService, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService subscriptionService)
    {
        return HandleCoreAsync(user, subscriptionService, CancellationToken.None);
    }

    private static async Task<IResult> HandleCoreAsync(
        ClaimsPrincipal user,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        string name = user.Identity?.Name ?? string.Empty;

        var subscriptions = await subscriptionService.ListMySubscriptionsAsync(name, name, cancellationToken);

        var response = new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.ToList()
        };

        return Results.Ok(response);
    }
}
