using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a subscription plan
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.Username = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("productHandle is required.");
        }

        var subscription = await subscriptionService.SubscribeAsync(request.Username, request.ProductHandle);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = Map(subscription)
        };

        return Results.Ok(response);
    }

    internal static SubscriptionDto Map(SubscriptionDetails details) => new()
    {
        SubscriptionId = details.SubscriptionId,
        ProductHandle = details.ProductHandle,
        ProductName = details.ProductName,
        State = details.State,
        PriceInCents = details.PriceInCents,
        Interval = details.Interval,
        IntervalUnit = details.IntervalUnit,
        ActivatedAt = details.ActivatedAt,
        CurrentPeriodEndsAt = details.CurrentPeriodEndsAt,
        NextBillingDate = details.NextBillingDate
    };
}
