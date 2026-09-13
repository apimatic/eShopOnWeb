using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a subscription for the authenticated user
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionCreateEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioClient maxioClient) =>
            {
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(401)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient maxioClient)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            response.ErrorMessage = "No HTTP context available.";
            return Results.Json(response, statusCode: 500);
        }

        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub")
            ?? httpContext.User.Identity?.Name;
        var userEmail = httpContext.User.FindFirstValue(ClaimTypes.Email)
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? userId;
        var userName = httpContext.User.FindFirstValue(ClaimTypes.GivenName) ?? "User";
        var userSurname = httpContext.User.FindFirstValue(ClaimTypes.Surname) ?? "";

        if (string.IsNullOrWhiteSpace(userId))
        {
            response.ErrorMessage = "Unable to determine authenticated user identity.";
            return Results.Json(response, statusCode: 400);
        }

        var reference = $"eshop:{userId}";

        var customer = await maxioClient.FindCustomerByReferenceAsync(reference);

        if (customer == null)
        {
            customer = await maxioClient.CreateCustomerAsync(new MaxioCustomerCreate
            {
                FirstName = userName,
                LastName = userSurname,
                Email = userEmail ?? $"{userId}@eshoponweb.local",
                Reference = reference
            });
        }

        // Create a bogus test payment profile for the customer (required for sandbox)
        var paymentProfile = await maxioClient.CreatePaymentProfileAsync(new MaxioPaymentProfileCreate
        {
            CustomerId = customer.Id,
            FirstName = userName,
            LastName = userSurname,
            FullNumber = "4111111111111111",
            ExpirationMonth = 12,
            ExpirationYear = 2030,
            CurrentVault = "bogus",
            VaultToken = "1"
        });

        try
        {
            var subscription = await maxioClient.CreateSubscriptionAsync(new MaxioSubscriptionCreate
            {
                ProductHandle = request.ProductHandle,
                CustomerId = customer.Id,
                PaymentProfileId = paymentProfile.Id
            });

            response.Subscription = MapSubscription(subscription);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = $"Failed to create subscription: {ex.Message}";
            return Results.Json(response, statusCode: 500);
        }
    }

    private static SubscriptionDto MapSubscription(MaxioSubscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            PlanName = sub.Product?.Name ?? "Unknown",
            PlanHandle = sub.Product?.Handle,
            Price = (sub.Product?.PriceInCents ?? 0) / 100m,
            CurrentPeriodEndsAt = ParseDateTime(sub.CurrentPeriodEndsAt),
            NextAssessmentAt = ParseDateTime(sub.NextAssessmentAt),
            ActivatedAt = ParseDateTime(sub.ActivatedAt),
            CreatedAt = ParseDateTime(sub.CreatedAt)
        };
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, out var dt)) return dt;
        return null;
    }
}
