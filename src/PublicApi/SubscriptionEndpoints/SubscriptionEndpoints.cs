using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpoints
{
    public static void MapSubscriptionEndpoints(this IEndpointRouteBuilder app, MaxioSettings settings)
    {
        // GET /api/subscription-plans
        app.MapGet("api/subscription-plans", async (IMaxioApiClient maxioClient) =>
        {
            var response = new ListSubscriptionPlansResponse();
            var products = await maxioClient.ListProductsAsync(settings.ProductFamilyHandle);
            foreach (var p in products)
            {
                response.Plans.Add(new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    Description = p.Description,
                    Price = p.PriceInCents / 100m,
                    IntervalUnit = p.IntervalUnit,
                    Interval = p.Interval,
                    RequireCreditCard = p.RequireCreditCard,
                    Taxable = p.Taxable,
                    ProductFamilyHandle = p.ProductFamilyHandle,
                    ProductFamilyName = p.ProductFamilyName
                });
            }
            return Results.Ok(response);
        });

        // POST /api/subscriptions
        app.MapPost("api/subscriptions", async ([FromBody] CreateSubscriptionRequest request, IMaxioApiClient maxioClient, HttpContext http) =>
        {
            var response = new CreateSubscriptionResponse();
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? http.User.FindFirstValue("sub")
                ?? http.User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(userId))
            {
                response.ErrorMessage = "Unable to determine user identity from token.";
                return Results.Unauthorized();
            }

            var customerReference = $"eshop-{userId}";
            var customerId = await EnsureCustomerAsync(maxioClient, userId, customerReference, http);
            if (customerId == null)
            {
                response.ErrorMessage = "Failed to ensure Maxio customer exists.";
                return Results.StatusCode(500);
            }

            var existingSubscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customerId.Value, "active");
            foreach (var existing in existingSubscriptions)
            {
                if (string.Equals(existing.ProductHandle, request.ProductHandle, StringComparison.OrdinalIgnoreCase))
                {
                    response.Subscription = MapToDto(existing);
                    return Results.Ok(response);
                }
            }

            try
            {
                var subscription = await maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
                {
                    ProductHandle = request.ProductHandle,
                    CustomerId = customerId.Value
                });
                response.Subscription = MapToDto(subscription);
                return Results.Ok(response);
            }
            catch (MaxioApiException ex)
            {
                response.ErrorMessage = $"Maxio error ({ex.StatusCode}): {ex.ResponseBody}";
                return Results.Json(response, statusCode: 400);
            }
            catch (Exception ex)
            {
                response.ErrorMessage = $"Failed to create subscription: {ex.Message}";
                return Results.StatusCode(500);
            }
        }).RequireAuthorization();

        // GET /api/my-subscriptions
        app.MapGet("api/my-subscriptions", async (IMaxioApiClient maxioClient, HttpContext http) =>
        {
            var response = new MySubscriptionsResponse();
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? http.User.FindFirstValue("sub")
                ?? http.User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var customerReference = $"eshop-{userId}";
            var existing = await maxioClient.FindCustomerByReferenceAsync(customerReference);
            if (existing == null)
                return Results.Ok(response);

            var subscriptions = await maxioClient.ListCustomerSubscriptionsAsync(existing.Id);
            foreach (var sub in subscriptions)
            {
                response.Subscriptions.Add(MapToDto(sub));
            }
            return Results.Ok(response);
        }).RequireAuthorization();
    }

    private static async Task<int?> EnsureCustomerAsync(
        IMaxioApiClient maxioClient, string userId, string customerReference, HttpContext http)
    {
        var existing = await maxioClient.FindCustomerByReferenceAsync(customerReference);
        if (existing != null)
            return existing.Id;

        var email = http.User.FindFirstValue(ClaimTypes.Email)
            ?? http.User.FindFirstValue("email")
            ?? http.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            email = $"{userId}@eshop.local";

        var firstName = http.User.FindFirstValue(ClaimTypes.GivenName)
            ?? http.User.FindFirstValue("given_name")
            ?? "eShop";
        var lastName = http.User.FindFirstValue(ClaimTypes.Surname)
            ?? http.User.FindFirstValue("family_name")
            ?? "User";

        var customer = await maxioClient.CreateCustomerAsync(customerReference, email, firstName, lastName);
        return customer.Id;
    }

    internal static SubscriptionDto MapToDto(MaxioSubscriptionDto sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            ProductName = sub.ProductName,
            ProductHandle = sub.ProductHandle,
            Price = sub.PriceInCents / 100m,
            CustomerId = sub.CustomerId,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CanceledAt = sub.CanceledAt,
            CreatedAt = sub.CreatedAt
        };
    }
}
