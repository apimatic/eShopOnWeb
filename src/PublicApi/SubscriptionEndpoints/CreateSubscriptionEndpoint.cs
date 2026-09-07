using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class CreateSubscriptionEndpoint
{
    public static void MapCreateSubscriptionEndpoint(this WebApplication app)
    {
        app.MapPost("/api/subscriptions", Handle)
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    private static async Task<IResult> Handle(
        CreateSubscriptionRequest request,
        HttpContext httpContext,
        IMaxioSubscriptionService subscriptionService,
        IRepository<Subscription> subscriptionRepository,
        UserManager<ApplicationUser> userManager)
    {
        var response = new CreateSubscriptionResponse
        {
            State = "",
            Message = ""
        };

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

            var maxioCustomer = await subscriptionService.GetOrCreateCustomerAsync(user.Id, user.Email ?? "");
            var subscription = await subscriptionService.CreateSubscriptionAsync(maxioCustomer.Id, request.ProductId);

            var localSubscription = new Subscription
            {
                UserId = user.Id,
                MaxioSubscriptionId = subscription.Id,
                MaxioProductId = subscription.ProductId,
                ProductHandle = subscription.ProductHandle,
                State = subscription.State,
                CurrentPrice = subscription.CurrentPrice,
                NextBillingDate = subscription.NextBillingDate,
                CreatedAt = subscription.CreatedAt,
                UpdatedAt = subscription.UpdatedAt
            };

            await subscriptionRepository.AddAsync(localSubscription);

            response.SubscriptionId = subscription.Id;
            response.State = subscription.State;
            response.Price = subscription.CurrentPrice;
            response.NextBillingDate = subscription.NextBillingDate;
            response.Message = "Subscription created successfully";

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = "Failed to create subscription", details = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public int ProductId { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public int SubscriptionId { get; set; }
    public required string State { get; set; }
    public decimal Price { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public required string Message { get; set; }
}
