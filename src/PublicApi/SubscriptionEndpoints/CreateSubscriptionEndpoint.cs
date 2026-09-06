using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class CreateSubscriptionEndpoint
{
    public static void MapCreateSubscriptionEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", CreateSubscription)
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> CreateSubscription(CreateSubscriptionRequest request, IMaxioSubscriptionService maxioService,
        HttpContext httpContext, UserManager<ApplicationUser> userManager,
        IRepository<ApplicationCore.Entities.SubscriptionAggregate.Subscription> subscriptionRepository)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

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

        var subscription = await maxioService.CreateSubscriptionAsync(
            userId,
            user.Email ?? string.Empty,
            user.UserName ?? string.Empty,
            user.UserName ?? string.Empty,
            request.PlanHandle);

        if (subscription == null)
        {
            return Results.BadRequest(new { error = "Failed to create subscription" });
        }

        var dbSubscription = new ApplicationCore.Entities.SubscriptionAggregate.Subscription
        {
            UserId = userId,
            MaxioSubscriptionId = subscription.Id,
            MaxioCustomerId = subscription.CustomerId,
            PlanHandle = request.PlanHandle,
            Status = subscription.Status,
            Price = subscription.Price,
            CreatedAt = subscription.CreatedAt,
            NextBillingDate = subscription.NextBillingDate,
        };

        await subscriptionRepository.AddAsync(dbSubscription);

        response.Success = true;
        response.SubscriptionId = subscription.Id;
        response.Status = subscription.Status;
        response.Price = subscription.Price;
        response.NextBillingDate = subscription.NextBillingDate;

        return Results.Ok(response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime? NextBillingDate { get; set; }
}
