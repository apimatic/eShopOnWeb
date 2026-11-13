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
/// The authenticated shopper's subscriptions, as reported by the billing system of record.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billing, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billing) =>
        HandleAsync(user, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var result = await billing.GetSubscriptionsAsync(userName, cancellationToken);

        var response = new ListMySubscriptionsResponse
        {
            BillingCustomerId = result.BillingCustomerId
        };
        response.Subscriptions.AddRange(result.Subscriptions.Select(SubscriptionDto.From));
        response.Pending.AddRange(result.Pending.Select(PendingSubscriptionDto.From));

        return Results.Ok(response);
    }
}
