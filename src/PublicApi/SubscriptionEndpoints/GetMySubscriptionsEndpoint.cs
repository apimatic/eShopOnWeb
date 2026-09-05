using System;
using System.Collections.Generic;
using System.Security.Claims;
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
/// Get subscriptions for the authenticated user
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var request = new GetMySubscriptionsRequest { UserId = userId };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(request.UserId);
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(new SubscriptionDetailResponse
            {
                Id = subscription.Id,
                MaxioSubscriptionId = subscription.MaxioSubscriptionId,
                ProductHandle = subscription.ProductHandle,
                State = subscription.State,
                Price = subscription.Price,
                PriceDisplay = subscription.PriceDisplay,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt
            });
        }

        return Results.Ok(response);
    }
}

public class GetMySubscriptionsRequest : BaseRequest
{
    public string UserId { get; set; } = string.Empty;
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDetailResponse> Subscriptions { get; } = [];
}
