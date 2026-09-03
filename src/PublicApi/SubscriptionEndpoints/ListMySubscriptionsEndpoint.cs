using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the current user's subscriptions, read from Maxio (the system of record).
/// GET /api/my-subscriptions (JWT-authenticated; the user is the token's identity).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, string, CancellationToken>
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
            (ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(userName, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string userName, CancellationToken cancellationToken)
    {
        try
        {
            var subscriber = new SubscriberIdentity(userName);
            var subscriptions = await _billingService.GetSubscriptionsAsync(subscriber, cancellationToken);

            var response = new ListMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
            };
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return ex.ToResult();
        }
    }
}
