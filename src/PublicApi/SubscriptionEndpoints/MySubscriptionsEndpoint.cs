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

public class MySubscriptionsEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", GetMySubscriptions)
            .Produces<MySubscriptionsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithTags("Subscriptions")
            .WithName("GetMySubscriptions")
            .RequireAuthorization();
    }

    private static async Task<IResult> GetMySubscriptions(
        IMaxioSubscriptionService subscriptionService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var userEmail = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
            if (string.IsNullOrEmpty(userEmail))
                return Results.BadRequest(new ErrorResponse { Message = "User email claim is missing" });

            var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userId, userEmail, ct);

            var response = new MySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => new UserSubscriptionResponse
                {
                    SubscriptionId = s.Id,
                    State = s.State,
                    PlanHandle = s.PlanHandle,
                    NextBillingAt = s.NextBillingAt,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    ActivatedAt = s.ActivatedAt
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch (MaxioSubscriptionException ex)
        {
            var statusCode = ex.StatusCode ?? StatusCodes.Status500InternalServerError;
            return Results.Json(
                new ErrorResponse { Message = ex.Message },
                statusCode: statusCode);
        }
    }

    public class MySubscriptionsResponse
    {
        public required List<UserSubscriptionResponse> Subscriptions { get; set; }
    }

    public class UserSubscriptionResponse
    {
        public required int SubscriptionId { get; set; }
        public required string State { get; set; }
        public required string PlanHandle { get; set; }
        public DateTimeOffset? NextBillingAt { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
    }

    public class ErrorResponse
    {
        public required string Message { get; set; }
    }
}
