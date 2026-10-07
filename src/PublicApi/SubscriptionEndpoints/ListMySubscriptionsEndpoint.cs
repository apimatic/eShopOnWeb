using System;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the signed-in shopper's subscriptions as recorded by the billing
/// system (all states). Empty until the user has subscribed.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), user, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, null, subscriptionService, CancellationToken.None);
    }

    private static async Task<IResult> HandleAsync(
        ListMySubscriptionsRequest request, ClaimsPrincipal? user, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var username = user?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.ListSubscriptionsForUserAsync(username, cancellationToken);
            response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                SubscriptionId = s.SubscriptionId,
                Reference = s.Reference,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                ProductPriceInCents = s.ProductPriceInCents,
                ProductPrice = s.ProductPriceInCents / 100m,
                State = s.State,
                NextBillingDate = s.NextBillingDate,
                ActivatedAt = s.ActivatedAt
            }).ToList();
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointErrors.ToResult(ex);
        }
    }
}
