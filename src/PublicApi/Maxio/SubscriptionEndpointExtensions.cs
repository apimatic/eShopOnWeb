using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class SubscriptionEndpointExtensions
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioClient maxioClient, MaxioSettings settings) =>
        {
            try
            {
                var products = await maxioClient.GetProductsByFamilyAsync(settings.ProductFamilyHandle);
                var plans = products.Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit,
                    RequireCreditCard = p.RequireCreditCard,
                    Taxable = p.Taxable,
                    ProductFamilyName = p.ProductFamily?.Name
                }).ToList();

                return Results.Ok(new { plans });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to retrieve subscription plans: {ex.Message}");
            }
        })
        .Produces<List<SubscriptionPlanDto>>()
        .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, IMaxioClient maxioClient, HttpContext httpContext) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? httpContext.User.FindFirstValue(ClaimTypes.Name);

            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var userEmail = httpContext.User.FindFirstValue(ClaimTypes.Email) ?? $"{userId}@placeholder.local";
            var userFullName = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? userId;

            try
            {
                var customerReference = $"eshop-{userId}";
                var customer = await maxioClient.GetCustomerByReferenceAsync(customerReference);

                if (customer == null)
                {
                    var nameParts = userFullName.Split(' ', 2);
                    var firstName = nameParts.Length > 0 ? nameParts[0] : "Unknown";
                    var lastName = nameParts.Length > 1 ? nameParts[1] : "User";
                    customer = await maxioClient.CreateCustomerAsync(customerReference, userEmail, firstName, lastName);
                }

                var subscription = await maxioClient.CreateSubscriptionAsync(customer.Id, request.ProductHandle);

                var dto = new SubscriptionDto
                {
                    Id = subscription.Id,
                    State = subscription.State,
                    ProductPriceInCents = subscription.ProductPriceInCents,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    ActivatedAt = subscription.ActivatedAt,
                    CreatedAt = subscription.CreatedAt,
                    CanceledAt = subscription.CanceledAt,
                    ProductHandle = subscription.Product?.Handle ?? subscription.ProductHandle,
                    ProductName = subscription.Product?.Name ?? subscription.ProductName,
                    CustomerId = subscription.CustomerId,
                    CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents,
                    Currency = subscription.Currency
                };

                return Results.Ok(new { subscription = dto });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to create subscription: {ex.Message}");
            }
        })
        .Produces<SubscriptionDto>()
        .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioClient maxioClient, HttpContext httpContext) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? httpContext.User.FindFirstValue(ClaimTypes.Name);

            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            try
            {
                var customerReference = $"eshop-{userId}";
                var customer = await maxioClient.GetCustomerByReferenceAsync(customerReference);

                if (customer == null)
                    return Results.Ok(new { subscriptions = Array.Empty<SubscriptionDto>() });

                var subscriptions = await maxioClient.GetCustomerSubscriptionsAsync(customer.Id);

                var dtos = subscriptions.Select(s => new SubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    ProductPriceInCents = s.ProductPriceInCents,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.NextAssessmentAt,
                    ActivatedAt = s.ActivatedAt,
                    CreatedAt = s.CreatedAt,
                    CanceledAt = s.CanceledAt,
                    ProductHandle = s.Product?.Handle ?? s.ProductHandle,
                    ProductName = s.Product?.Name ?? s.ProductName,
                    CustomerId = s.CustomerId,
                    CurrentBillingAmountInCents = s.CurrentBillingAmountInCents,
                    Currency = s.Currency
                }).ToList();

                return Results.Ok(new { subscriptions = dtos });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to retrieve subscriptions: {ex.Message}");
            }
        })
        .Produces<List<SubscriptionDto>>()
        .WithTags("SubscriptionEndpoints");

        return app;
    }
}
