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
/// Lists the authenticated shopper's subscriptions. Returns an empty list when the shopper is not
/// yet a Maxio customer.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                var request = new GetMySubscriptionsRequest { UserName = user.FindFirstValue(ClaimTypes.Name) };
                return await HandleAsync(request, billing, ct);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(GetMySubscriptionsRequest request, ISubscriptionBillingService billing)
        => HandleAsync(request, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, ISubscriptionBillingService billing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
            return Results.Unauthorized();

        var response = new GetMySubscriptionsResponse(request.CorrelationId());
        var subscriber = SubscriberIdentity.FromUserName(request.UserName);

        var subscriptions = await billing.GetSubscriptionsAsync(subscriber, ct);
        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();

        return Results.Ok(response);
    }
}
