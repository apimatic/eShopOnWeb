using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly ILogger<ListMySubscriptionsEndpoint> _logger;

    public ListMySubscriptionsEndpoint(ILogger<ListMySubscriptionsEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioApiClient maxioClient) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? httpContext.User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                try
                {
                    var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? $"user_{userId}@eshop.local";
                    var firstName = httpContext.User.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
                    var lastName = httpContext.User.FindFirst(ClaimTypes.Surname)?.Value ?? "Account";

                    var customer = await maxioClient.GetOrCreateCustomerAsync(userId, firstName, lastName, email);
                    if (customer == null)
                        return Results.Ok(new MySubscriptionsResponse { Subscriptions = new() });

                    var subscriptions = await maxioClient.GetCustomerSubscriptionsAsync(customer.Id);

                    var response = new MySubscriptionsResponse
                    {
                        Subscriptions = subscriptions
                            .Select(s => new UserSubscriptionDto
                            {
                                SubscriptionId = s.Id,
                                ProductName = s.Product?.Name ?? "Unknown",
                                PricePerMonth = s.Product_price_in_cents / 100m,
                                State = s.State,
                                NextBillingDate = s.Current_period_ends_at?.ToString("o"),
                                CreatedAt = s.Created_at.ToString("o")
                            })
                            .ToList()
                    };

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting subscriptions");
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListMySubscriptions")
            .RequireAuthorization();
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}

public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal PricePerMonth { get; set; }
    public string State { get; set; } = string.Empty;
    public string? NextBillingDate { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public class MySubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
