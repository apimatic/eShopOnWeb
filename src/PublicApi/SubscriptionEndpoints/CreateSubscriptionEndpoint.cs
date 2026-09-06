using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
}

public class CreateSubscriptionEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioService maxioService, UserManager<ApplicationUser> userManager, IRepository<Subscription> subscriptionRepository, HttpContext context) =>
            {
                var userIdClaim = context.User?.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByNameAsync(userIdClaim);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var maxioSubscription = await maxioService.CreateSubscriptionAsync(
                        user.Id,
                        user.UserName ?? "User",
                        user.UserName ?? "User",
                        user.Email ?? "",
                        request.PlanHandle);

                    var customerId = await maxioService.GetOrCreateMaxioCustomerAsync(
                        user.Id,
                        user.UserName ?? "User",
                        user.UserName ?? "User",
                        user.Email ?? "");

                    if (customerId == null)
                    {
                        return Results.BadRequest("Failed to get or create customer");
                    }

                    var subscription = new Subscription
                    {
                        UserId = user.Id,
                        MaxioSubscriptionId = maxioSubscription.Id,
                        MaxioCustomerId = customerId.Value,
                        PlanHandle = request.PlanHandle,
                        State = maxioSubscription.State,
                        NextBillingDate = maxioSubscription.NextBillingDate,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await subscriptionRepository.AddAsync(subscription);

                    return Results.Ok(new CreateSubscriptionResponse
                    {
                        SubscriptionId = maxioSubscription.Id,
                        State = maxioSubscription.State,
                        PlanName = maxioSubscription.PlanName,
                        NextBillingDate = maxioSubscription.NextBillingDate
                    });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest($"Error creating subscription: {ex.Message}");
                }
            })
           .WithName("CreateSubscription")
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithOpenApi()
           .WithSummary("Create a subscription")
           .WithDescription("Creates a new subscription for the authenticated user");
    }
}
