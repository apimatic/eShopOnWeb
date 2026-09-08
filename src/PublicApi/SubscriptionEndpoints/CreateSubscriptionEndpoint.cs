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
/// Subscribes the authenticated user to a plan (idempotent: replaying the same
/// request returns the existing enrollment instead of creating a duplicate).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal caller) =>
            {
                return await HandleAsync(request, subscriptionService, caller);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(401)
            .Produces(404)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal caller)
    {
        var userId = caller.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "A plan handle is required. Call GET /api/subscription-plans to see the available plans." });
        }

        var result = await subscriptionService.SubscribeAsync(userId, request.PlanHandle.Trim());

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ToDto(result),
        };

        return Results.Ok(response);
    }

    internal static SubscriptionDto ToDto(SubscriptionResult result) => new SubscriptionDto
    {
        MaxioSubscriptionId = result.MaxioSubscriptionId,
        MaxioCustomerId = result.MaxioCustomerId,
        PlanHandle = result.PlanHandle,
        PlanName = result.PlanName,
        Price = result.Price,
        IntervalUnit = result.IntervalUnit,
        State = result.State,
        NextBillingAt = result.NextBillingAt,
        CurrentPeriodStartedAt = result.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = result.CurrentPeriodEndsAt,
        CreatedAt = result.CreatedAt,
        StatusMessage = result.NewlyCreated
            ? $"Subscribed to {result.PlanName}."
            : $"Already subscribed to {result.PlanName}; returning the existing enrollment.",
    };
}
