using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class GetUserSubscriptionsEndpoint
{
    public static void MapGetUserSubscriptionsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", GetSubscriptions)
            .RequireAuthorization()
            .Produces<GetUserSubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> GetSubscriptions(HttpContext httpContext, IMaxioSubscriptionService maxioService,
        UserManager<ApplicationUser> userManager, IReadRepository<ApplicationCore.Entities.SubscriptionAggregate.Subscription> subscriptionRepository)
    {
        var request = new GetUserSubscriptionsRequest();
        var response = new GetUserSubscriptionsResponse(request.CorrelationId());

        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirst(ClaimTypes.Name);
        if (userIdClaim == null)
        {
            return Results.Unauthorized();
        }

        var userId = userIdClaim.Value;
        var user = await userManager.FindByIdAsync(userId) ?? await userManager.FindByNameAsync(userId);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var spec = new UserSubscriptionsSpecification(userId);
        var subscriptions = await subscriptionRepository.ListAsync(spec);

        response.Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
        {
            Id = s.Id,
            MaxioSubscriptionId = s.MaxioSubscriptionId,
            PlanHandle = s.PlanHandle,
            Status = s.Status,
            Price = s.Price,
            CreatedAt = s.CreatedAt,
            NextBillingDate = s.NextBillingDate,
            CanceledAt = s.CanceledAt
        }).ToList();

        return Results.Ok(response);
    }
}

public class GetUserSubscriptionsRequest : BaseRequest
{
}

public class GetUserSubscriptionsResponse : BaseResponse
{
    public GetUserSubscriptionsResponse() { }
    public GetUserSubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CanceledAt { get; set; }
}
