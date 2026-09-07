using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(ILogger<CreateSubscriptionEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioApiClient maxioClient) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? httpContext.User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                if (string.IsNullOrEmpty(request.ProductHandle))
                    return Results.BadRequest(new { error = "ProductHandle is required" });

                try
                {
                    var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? $"user_{userId}@eshop.local";
                    var firstName = httpContext.User.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
                    var lastName = httpContext.User.FindFirst(ClaimTypes.Surname)?.Value ?? "Account";

                    var customer = await maxioClient.GetOrCreateCustomerAsync(userId, firstName, lastName, email);
                    if (customer == null)
                        return Results.BadRequest(new { error = "Failed to create or retrieve customer" });

                    var subscription = await maxioClient.CreateSubscriptionAsync(customer.Id, request.ProductHandle);

                    return Results.Ok(new CreateSubscriptionResponse
                    {
                        SubscriptionId = subscription.Id,
                        State = subscription.State,
                        PricePerMonth = subscription.Product_price_in_cents / 100m,
                        NextBillingDate = subscription.Current_period_ends_at?.ToString("o"),
                        Message = "Subscription created successfully"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating subscription");
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription")
            .RequireAuthorization();
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}

public class CreateSubscriptionRequest
{
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal PricePerMonth { get; set; }
    public string? NextBillingDate { get; set; }
    public string Message { get; set; } = string.Empty;
}
