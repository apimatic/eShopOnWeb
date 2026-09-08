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
/// Lists the authenticated shopper's subscriptions (empty if they have no Maxio customer yet).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billingService, CancellationToken cancellationToken) =>
            {
                var request = new ListMySubscriptionsRequest
                {
                    UserName = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name
                };
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioBillingService billingService)
        => HandleAsync(request, billingService, default);

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioBillingService billingService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        var subscriber = SubscriberIdentity.FromUserName(request.UserName);
        var subscriptions = await billingService.GetSubscriptionsAsync(subscriber, cancellationToken);

        var response = new ListMySubscriptionsResponse(request.CorrelationId());
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(subscription.ToDto());
        }

        return Results.Ok(response);
    }
}
