using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, SubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, subscriptionService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription")
            .RequireAuthorization();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriptionService subscriptionService, HttpContext httpContext)
    {
        try
        {
            var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
            var userNameClaim = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            var userId = httpContext.User.FindFirst("sub")?.Value ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userEmail) || string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var (firstName, lastName) = ParseName(userNameClaim ?? userEmail);

            var customer = await subscriptionService.GetOrCreateCustomerAsync(
                userEmail,
                firstName,
                lastName,
                userId);

            var subscription = await subscriptionService.CreateSubscriptionAsync(
                customer.MaxioCustomerId,
                request.ProductHandle,
                userId);

            return Results.Created($"/api/subscriptions/{subscription.Id}", new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                PriceInCents = subscription.PriceInCents,
                State = subscription.State,
                ActivatedAt = subscription.ActivatedAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                Message = "Subscription created successfully"
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static (string, string) ParseName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return (parts[0], parts[parts.Length - 1]);
        if (parts.Length == 1)
            return (parts[0], "");
        return ("User", "");
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = "";
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public long PriceInCents { get; set; }
    public string State { get; set; } = "";
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public string Message { get; set; } = "";
}
