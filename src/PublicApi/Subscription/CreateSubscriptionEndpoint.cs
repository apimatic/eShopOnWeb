using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.Subscription;

public static class CreateSubscriptionEndpoint
{
    public static void MapCreateSubscription(this WebApplication app)
    {
        app.MapPost("api/subscriptions",
            CreateSubscription)
            .WithName("CreateSubscription")
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> CreateSubscription(
        CreateSubscriptionRequestBody body,
        HttpContext httpContext,
        MaxioSubscriptionService subscriptionService)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
        {
            return Results.Unauthorized();
        }

        try
        {
            if (string.IsNullOrEmpty(body.PlanHandle))
            {
                return Results.BadRequest(new { error = "PlanHandle is required" });
            }

            var subscription = await subscriptionService.CreateOrUpdateSubscriptionAsync(
                userId,
                email,
                body.PlanHandle);

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.SubscriptionId,
                CustomerId = subscription.CustomerId,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                PriceInCents = subscription.PriceInCents,
                Price = subscription.Price,
                NextBillingDate = subscription.NextBillingDate,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Ok(response);
        }
        catch (MaxioServiceException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}

public class CreateSubscriptionRequestBody
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
