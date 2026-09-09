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
/// Lists the authenticated caller's subscriptions. Returns an empty list when the user has never
/// subscribed (no Maxio customer yet).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly ISubscriptionBillingService _billing;

    public ListMySubscriptionsEndpoint(ISubscriptionBillingService billing)
    {
        _billing = billing;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, CancellationToken ct) => await HandleAsync(user, ct))
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user) => HandleAsync(user, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var subscriber = SubscriberIdentityFactory.FromPrincipal(user);
        if (subscriber is null)
            return Results.Unauthorized();

        try
        {
            var subscriptions = await _billing.GetMySubscriptionsAsync(subscriber, cancellationToken);
            return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = new(subscriptions) });
        }
        catch (SubscriptionBillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.SuggestedStatusCode);
        }
    }
}
