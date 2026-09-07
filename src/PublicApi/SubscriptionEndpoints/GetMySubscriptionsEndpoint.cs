using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class GetMySubscriptionsEndpoint
{
    public static void MapMySubscriptionsEndpoint(this WebApplication app)
    {
        app.MapGet("/api/my-subscriptions", Handle)
            .RequireAuthorization()
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    private static async Task<IResult> Handle(
        HttpContext httpContext,
        IReadRepository<Subscription> subscriptionRepository,
        UserManager<ApplicationUser> userManager)
    {
        var response = new GetMySubscriptionsResponse();

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByNameAsync(userId);
            if (user == null)
            {
                return Results.NotFound("User not found");
            }

            var subscriptions = await subscriptionRepository.ListAsync(
                new SubscriptionsByUserSpecification(user.Id));

            response.Subscriptions.AddRange(subscriptions.Select(s => new UserSubscriptionDto
            {
                Id = s.Id,
                MaxioSubscriptionId = s.MaxioSubscriptionId,
                ProductHandle = s.ProductHandle,
                State = s.State,
                Price = s.CurrentPrice,
                NextBillingDate = s.NextBillingDate,
                CreatedAt = s.CreatedAt
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = "Failed to retrieve subscriptions", details = ex.Message });
        }
    }
}

public class SubscriptionsByUserSpecification : Specification<Subscription>
{
    public SubscriptionsByUserSpecification(string userId)
    {
        Query.Where(s => s.UserId == userId);
    }
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public required string ProductHandle { get; set; }
    public required string State { get; set; }
    public decimal Price { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
