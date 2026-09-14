using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated buyer's subscriptions, refreshed from the billing
/// system of record.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(userName);
            foreach (var subscription in subscriptions)
            {
                response.Subscriptions.Add(new SubscriptionDto(
                    subscription.BuyerId, subscription.MaxioSubscriptionId, subscription.ProductHandle,
                    subscription.ProductName, subscription.PriceInCents, subscription.Price,
                    subscription.Interval, subscription.IntervalUnit, subscription.State, subscription.Currency,
                    subscription.NextBillingDate, subscription.ActivatedAt, subscription.CanceledAt,
                    subscription.AlreadyExisted));
            }
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(statusCode: 502, title: "The billing system could not be reached.",
                detail: string.Join("; ", ex.Errors.Count > 0 ? ex.Errors : new[] { ex.Message }),
                extensions: new Dictionary<string, object?> { ["correlationId"] = request.CorrelationId() });
        }
    }
}
