using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext,
                IMaxioService maxioService, UserManager<ApplicationUser> userManager,
                ILogger<CreateSubscriptionEndpoint> logger) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    logger.LogWarning("No user ID in token");
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    logger.LogWarning($"User not found: {userId}");
                    return Results.NotFound("User not found");
                }

                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                {
                    return Results.BadRequest("ProductHandle is required");
                }

                logger.LogInformation($"Creating subscription for user {userId} on plan {request.ProductHandle}");

                var customer = await maxioService.GetOrCreateCustomerAsync(
                    userId,
                    user.Email ?? "",
                    user.UserName ?? "User",
                    user.UserName ?? "User");

                if (customer == null)
                {
                    logger.LogError($"Failed to create or find customer for user {userId}");
                    return Results.BadRequest("Failed to create customer in billing system");
                }

                var subscription = await maxioService.CreateSubscriptionAsync(customer.Id, request.ProductHandle);
                if (subscription == null)
                {
                    logger.LogError($"Failed to create subscription for customer {customer.Id}");
                    return Results.BadRequest("Failed to create subscription");
                }

                logger.LogInformation($"Successfully created subscription {subscription.Id} for user {userId}");

                var response = new CreateSubscriptionResponse
                {
                    SubscriptionId = subscription.Id,
                    CustomerId = subscription.CustomerId,
                    State = subscription.State,
                    CreatedAt = subscription.CreatedAt,
                    NextBillingAt = subscription.NextBillingAt
                };

                return Results.Created($"/api/my-subscriptions/{subscription.Id}", response);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException();
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = "";
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime NextBillingAt { get; set; }
}
