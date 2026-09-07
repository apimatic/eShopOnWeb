using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", Subscribe)
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithTags("Subscriptions")
            .WithName("Subscribe")
            .RequireAuthorization();
    }

    private static async Task<IResult> Subscribe(
        IMaxioSubscriptionService subscriptionService,
        SubscribeRequest request,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(request.PlanHandle))
                return Results.BadRequest(new ErrorResponse { Message = "Plan handle is required" });

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var userEmail = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
            if (string.IsNullOrEmpty(userEmail))
                return Results.BadRequest(new ErrorResponse { Message = "User email claim is missing" });

            var userName = user.FindFirst(ClaimTypes.Name)?.Value ?? userEmail;

            var subscription = await subscriptionService.SubscribeAsync(
                userId, userEmail, userName, request.PlanHandle, ct);

            var response = new SubscribeResponse
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                PlanHandle = subscription.PlanHandle,
                NextBillingAt = subscription.NextBillingAt,
                ActivatedAt = subscription.ActivatedAt
            };

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (MaxioSubscriptionException ex)
        {
            var statusCode = ex.StatusCode ?? StatusCodes.Status500InternalServerError;
            return Results.Json(
                new ErrorResponse { Message = ex.Message },
                statusCode: statusCode);
        }
    }

    public class SubscribeRequest
    {
        public required string PlanHandle { get; set; }
    }

    public class SubscribeResponse
    {
        public required int SubscriptionId { get; set; }
        public required string State { get; set; }
        public required string PlanHandle { get; set; }
        public DateTimeOffset? NextBillingAt { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
    }

    public class ErrorResponse
    {
        public required string Message { get; set; }
    }
}
