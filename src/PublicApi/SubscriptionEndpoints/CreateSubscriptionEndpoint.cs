using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = "";
}

/// <summary>
/// Create Subscription
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, IMaxioApiService maxioService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, maxioService, httpContext);
            })
           .Produces<CreateSubscriptionResponse>()
           .WithName("CreateSubscription")
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioApiService maxioService)
    {
        return await HandleAsync(request, maxioService, null);
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioApiService maxioService, HttpContext? httpContext)
    {
        try
        {
            if (httpContext == null)
            {
                return Results.BadRequest(new { error = "HttpContext is required" });
            }

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? userId;

            var parts = userName.Split(' ');
            var firstName = parts.Length > 0 ? parts[0] : "User";
            var lastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";

            var customer = await maxioService.GetOrCreateCustomerAsync(userId, firstName, lastName, userEmail);

            var subscription = await maxioService.CreateSubscriptionAsync(customer.Id, request.ProductHandle);

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                CustomerId = customer.Id,
                State = subscription.State,
                ProductHandle = subscription.Product?.Handle ?? "",
                ProductName = subscription.Product?.Name ?? "",
                Price = FormatPrice(subscription.ProductPriceInCents),
                NextBillingDate = subscription.NextAssessmentAt ?? "",
                ActivatedAt = subscription.ActivatedAt ?? "",
                CreatedAt = subscription.CreatedAt ?? ""
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private string FormatPrice(long priceCents)
    {
        return $"${priceCents / 100m:F2}";
    }
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Price { get; set; } = "";
    public string NextBillingDate { get; set; } = "";
    public string ActivatedAt { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
