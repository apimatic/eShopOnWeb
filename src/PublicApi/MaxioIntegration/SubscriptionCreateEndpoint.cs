using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest>
{
    private readonly IMaxioApiClient _maxioClient;

    public SubscriptionCreateEndpoint(IMaxioApiClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (SubscriptionCreateRequest request, HttpRequest httpRequest) =>
        {
            return await HandleAsync(request, httpRequest);
        })
        .Produces<SubscriptionCreateResponse>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, HttpRequest httpRequest)
    {
        var response = new SubscriptionCreateResponse(request.CorrelationId());

        var userId = httpRequest.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            response.IsSuccess = false;
            response.ErrorMessage = "User not authenticated.";
            return Results.Unauthorized();
        }

        var user = httpRequest.HttpContext.User;
        var email = user.FindFirstValue(ClaimTypes.Name) ?? $"{userId}@unknown.com";
        var displayName = user.Identity?.Name ?? "Unknown User";
        var nameParts = displayName.Split(' ', 2);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "Unknown";
        var lastName = nameParts.Length > 1 ? nameParts[1] : "User";

        try
        {
            // 1. Find or create Maxio customer (idempotent by user ID reference)
            var customer = await _maxioClient.FindCustomerByReferenceAsync(userId);
            if (customer == null)
            {
                customer = await _maxioClient.CreateCustomerAsync(firstName, lastName, email, userId);
            }

            // 2. Create subscription
            var subscriptionRequest = new MaxioCreateSubscriptionRequest
            {
                ProductHandle = request.ProductHandle,
                CustomerId = customer.Id
            };

            var subscription = await _maxioClient.CreateSubscriptionAsync(subscriptionRequest);

            // 3. Build response
            response.IsSuccess = true;
            response.Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductHandle = request.ProductHandle,
                ProductName = subscription.Product?.Name ?? request.ProductHandle,
                PriceInCents = subscription.ProductPriceInCents,
                Price = subscription.ProductPriceInCents / 100m,
                NextBillingDate = subscription.NextAssessmentAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt
            };
        }
        catch (HttpRequestException ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = $"Maxio API error: {ex.Message}";
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = $"Subscription creation failed: {ex.Message}";
        }

        return Results.Ok(response);
    }
}
