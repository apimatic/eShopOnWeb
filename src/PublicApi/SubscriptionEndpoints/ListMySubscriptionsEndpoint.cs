using System;
using System.Collections.Generic;
using System.Linq;
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

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioService maxioService,
                UserManager<ApplicationUser> userManager,
                ILogger<ListMySubscriptionsEndpoint> logger) =>
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

                logger.LogInformation($"Fetching subscriptions for user {userId}");

                var customer = await maxioService.GetOrCreateCustomerAsync(
                    userId,
                    user.Email ?? "",
                    user.UserName ?? "User",
                    user.UserName ?? "User");

                if (customer == null)
                {
                    logger.LogWarning($"No customer found for user {userId}");
                    return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = new List<SubscriptionDto>() });
                }

                var subscriptions = await maxioService.ListCustomerSubscriptionsAsync(customer.Id);
                var subscriptionDtos = subscriptions.Select(s => new SubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    ProductId = s.ProductId,
                    CreatedAt = s.CreatedAt,
                    NextBillingAt = s.NextBillingAt
                }).ToList();

                var response = new ListMySubscriptionsResponse
                {
                    Subscriptions = subscriptionDtos
                };

                return Results.Ok(response);
            })
            .RequireAuthorization()
            .Produces<ListMySubscriptionsResponse>()
            .WithName("GetMySubscriptions")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService)
    {
        throw new NotImplementedException();
    }
}

public class ListMySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public int ProductId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextBillingAt { get; set; }
}
