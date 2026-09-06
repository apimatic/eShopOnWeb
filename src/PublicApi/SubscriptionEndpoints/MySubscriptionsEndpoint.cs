using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (string userId, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(userId, Guid.NewGuid()), subscriptionService);
            })
            .RequireAuthorization()
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithSummary("Get current user's subscriptions");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioSubscriptionService subscriptionService)
    {
        try
        {
            if (string.IsNullOrEmpty(request.UserId))
            {
                return Results.BadRequest(new { message = "User ID is required" });
            }

            // Get or find the Maxio customer ID for this user
            var customerId = await subscriptionService.EnsureCustomerExistsAsync(request.UserId, request.UserId);

            if (customerId is null or 0)
            {
                // Return empty list if customer doesn't exist yet
                return Results.Ok(new MySubscriptionsResponse(request.CorrelationId()) { Subscriptions = new() });
            }

            // Get customer's subscriptions
            var subscriptions = await subscriptionService.GetCustomerSubscriptionsAsync(customerId.Value);

            var response = new MySubscriptionsResponse(request.CorrelationId());
            response.Subscriptions.AddRange(subscriptions
                .Where(s => s.State == "active" || s.State == "trialing") // Only show active/trialing subscriptions
                .Select(s => new UserSubscriptionDto
                {
                    SubscriptionId = s.Id,
                    ProductName = s.ProductName,
                    ProductHandle = s.ProductHandle,
                    PriceInCents = s.ProductPriceInCents,
                    State = s.State,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.NextAssessmentAt,
                    CreatedAt = s.CreatedAt
                }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string userId, Guid correlationId) : base()
    {
        base._correlationId = correlationId;
        UserId = userId;
    }

    public string UserId { get; set; } = string.Empty;
}

public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public decimal? PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsResponse()
    {
    }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
