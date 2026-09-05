using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsyncInternal)
           .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status401Unauthorized)
           .WithName("CreateSubscription")
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => throw new NotImplementedException();

    private async Task<IResult> HandleAsyncInternal(
        CreateSubscriptionRequest request,
        HttpContext context,
        IMaxioService maxioService,
        CatalogContext catalogContext,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userEmail = context.User.FindFirst(ClaimTypes.Email)?.Value;
        var firstName = context.User.FindFirst("first_name")?.Value ?? "Unknown";
        var lastName = context.User.FindFirst("last_name")?.Value ?? "User";

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var plan = await catalogContext.SubscriptionPlans.FindAsync(request.PlanId);
            if (plan == null)
            {
                return Results.BadRequest(new { error = "Plan not found" });
            }

            var maxioCustomer = await maxioService.GetOrCreateCustomerAsync(
                userId,
                userEmail ?? "unknown@example.com",
                firstName,
                lastName);

            if (maxioCustomer == null)
            {
                return Results.BadRequest(new { error = "Failed to create customer in Maxio" });
            }

            var existingSubscription = await catalogContext.UserSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.SubscriptionPlanId == plan.Id);

            if (existingSubscription != null)
            {
                return Results.BadRequest(new { error = "User already has this subscription" });
            }

            var maxioSubscription = await maxioService.CreateSubscriptionAsync(
                maxioCustomer.Id,
                plan.MaxioProductId,
                $"{userId}-{plan.Id}-{DateTime.UtcNow.Ticks}");

            if (maxioSubscription == null)
            {
                return Results.BadRequest(new { error = "Failed to create subscription in Maxio" });
            }

            var userSubscription = new UserSubscription
            {
                UserId = userId,
                MaxioSubscriptionId = maxioSubscription.Id,
                MaxioCustomerId = maxioCustomer.Id,
                SubscriptionPlanId = plan.Id,
                State = maxioSubscription.State,
                BalanceInCents = maxioSubscription.BalanceInCents,
                CurrentPeriodEndsAt = maxioSubscription.CurrentPeriodEndsAt,
                NextAssessmentAt = maxioSubscription.NextAssessmentAt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            catalogContext.UserSubscriptions.Add(userSubscription);
            await catalogContext.SaveChangesAsync();

            var responseData = new CreateSubscriptionResponse
            {
                SubscriptionId = userSubscription.Id,
                MaxioSubscriptionId = maxioSubscription.Id,
                PlanName = plan.Name,
                State = maxioSubscription.State,
                NextBillingDate = maxioSubscription.NextAssessmentAt,
                PriceInCents = plan.PriceInCents
            };

            return Results.Created($"/api/subscriptions/{userSubscription.Id}", responseData);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            return Results.BadRequest(new { error = "Failed to create subscription", details = ex.Message });
        }
    }
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime NextBillingDate { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
}
