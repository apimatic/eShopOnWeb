using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated user's Maxio subscriptions
/// </summary>
public class SubscriptionListEndpoint : IEndpoint<IResult, SubscriptionListRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioService maxioService, ClaimsPrincipal user) =>
            {
                var userName = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                var request = new SubscriptionListRequest(userName);
                return await HandleAsync(request, maxioService);
            })
            .Produces<SubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionListRequest request, IMaxioService maxioService)
    {
        var subscriptions = await maxioService.GetMySubscriptionsAsync(request.UserName);

        var response = new SubscriptionListResponse(request.CorrelationId());
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductId = s.ProductId,
            ProductName = s.Product?.Name ?? string.Empty,
            ProductHandle = s.Product?.Handle ?? string.Empty,
            PriceInCents = s.ProductPriceInCents,
            BalanceInCents = s.BalanceInCents,
            TotalRevenueInCents = s.TotalRevenueInCents,
            NextAssessmentAt = s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            ActivatedAt = s.ActivatedAt,
            CanceledAt = s.CanceledAt,
            CancelAtEndOfPeriod = s.CancelAtEndOfPeriod,
            ExpiresAt = s.ExpiresAt,
            PaymentCollectionMethod = s.PaymentCollectionMethod,
            Currency = s.Currency,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        }));

        return Results.Ok(response);
    }
}
