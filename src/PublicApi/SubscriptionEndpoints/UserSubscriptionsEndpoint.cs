using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Linq;

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class UserSubscriptionsEndpointExtension
{
    public static void MapGetMySubscriptions(this WebApplication app)
    {
        app.MapGet("api/my-subscriptions", GetMySubscriptions)
            .WithName("GetMySubscriptions")
            .RequireAuthorization()
            .Produces<UserSubscriptionsResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> GetMySubscriptions(
        HttpContext httpContext,
        MaxioSubscriptionService service,
        AppIdentityDbContext dbContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        try
        {
            var subscriptions = await service.GetUserSubscriptionsAsync(userId);

            var plans = await dbContext.SubscriptionPlans.ToListAsync();
            var planDict = plans.ToDictionary(p => p.Id);

            var subscriptionDtos = subscriptions.ConvertAll(s =>
            {
                planDict.TryGetValue(s.SubscriptionPlanId, out var plan);
                return new SubscriptionDto
                {
                    Id = s.Id,
                    Status = s.Status,
                    PlanHandle = plan?.Handle ?? string.Empty,
                    PlanName = plan?.Name ?? string.Empty,
                    Price = plan?.Price ?? 0,
                    StartDate = s.StartDate,
                    NextBillingDate = s.NextBillingDate,
                    EndDate = s.EndDate
                };
            });

            var response = new UserSubscriptionsResponse(Guid.NewGuid())
            {
                Subscriptions = subscriptionDtos
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class UserSubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    public UserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }
}
