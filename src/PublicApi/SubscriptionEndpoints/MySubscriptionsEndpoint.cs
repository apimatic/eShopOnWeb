using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest { }

/// <summary>
/// Get User's Subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioApiService maxioService, HttpContext httpContext) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(), maxioService, httpContext);
            })
           .Produces<MySubscriptionsResponse>()
           .WithName("GetMySubscriptions")
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioApiService maxioService)
    {
        return await HandleAsync(request, maxioService, null);
    }

    private async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioApiService maxioService, HttpContext? httpContext)
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

            var subscriptions = await maxioService.ListCustomerSubscriptionsAsync(customer.Id);

            var response = new MySubscriptionsResponse
            {
                CustomerId = customer.Id,
                CustomerName = $"{customer.FirstName} {customer.LastName}".Trim(),
                Email = customer.Email
            };

            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
            {
                SubscriptionId = s.Id,
                State = s.State,
                ProductHandle = s.Product?.Handle ?? "",
                ProductName = s.Product?.Name ?? "",
                Price = FormatPrice(s.ProductPriceInCents),
                NextBillingDate = s.NextAssessmentAt ?? "",
                ActivatedAt = s.ActivatedAt ?? "",
                CreatedAt = s.CreatedAt ?? ""
            }));

            return Results.Ok(response);
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

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Price { get; set; } = "";
    public string NextBillingDate { get; set; } = "";
    public string ActivatedAt { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public class MySubscriptionsResponse
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string Email { get; set; } = "";
    public List<SubscriptionDto> Subscriptions { get; } = new();
}
