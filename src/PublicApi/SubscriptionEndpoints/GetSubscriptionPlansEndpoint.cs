using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpointsExtension
{
    public static void MapSubscriptionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");

        group.MapGet("/subscription-plans", GetSubscriptionPlans)
            .WithName("GetSubscriptionPlans")
            .WithMetadata(new SwaggerOperationAttribute(
                "List available subscription plans",
                "Retrieve all available subscription plans from Maxio"));

        group.MapPost("/subscriptions", CreateSubscription)
            .WithName("CreateSubscription")
            .WithMetadata(new SwaggerOperationAttribute(
                "Create a new subscription",
                "Subscribe the authenticated user to a selected plan"));

        group.MapGet("/my-subscriptions", GetMySubscriptions)
            .WithName("GetMySubscriptions")
            .WithMetadata(new SwaggerOperationAttribute(
                "Get user's subscriptions",
                "Retrieve all subscriptions for the authenticated user"));
    }

    private static async Task<IResult> GetSubscriptionPlans(MaxioAdvancedBillingClient maxioClient)
    {
        var response = new GetSubscriptionPlansResponse();

        try
        {
            var products = await maxioClient.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: default);

            response.Plans = products
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Product?.Id ?? 0,
                    Handle = p.Product?.Handle ?? "",
                    Name = p.Product?.Name ?? "",
                    Description = p.Product?.Description ?? "",
                    PriceInCents = (long)(p.Product?.PriceInCents ?? 0m),
                    Interval = p.Product?.Interval ?? 0,
                    IntervalUnit = p.Product?.IntervalUnit ?? ""
                })
                .ToList();

            response.Success = true;
            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int)(ex.Error.StatusCode));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CreateSubscription(
        SubscribeRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        MaxioAdvancedBillingClient maxioClient,
        Microsoft.eShopWeb.ApplicationCore.Interfaces.IRepository<Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription> repo)
    {
        var response = new CreateSubscriptionResponse();
        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            // Step 1: Ensure Maxio customer exists
            int maxioCustomerId = await EnsureCustomerExists(userId, maxioClient);

            // Step 2: Create subscription in Maxio
            var subscriptionRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    CustomerId = maxioCustomerId,
                    ProductId = request.ProductId
                }
            };

            var subscriptionResponse = await maxioClient.Subscriptions.CreateSubscription(
                body: subscriptionRequest,
                ct: default);

            if (subscriptionResponse?.Subscription == null)
            {
                return Results.BadRequest(new { error = "Failed to create subscription" });
            }

            // Step 3: Store subscription locally
            var subscription = new Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription
            {
                UserId = userId,
                MaxioCustomerId = maxioCustomerId,
                MaxioSubscriptionId = subscriptionResponse.Subscription.Id ?? 0,
                ProductHandle = request.ProductHandle ?? "",
                ProductName = request.ProductName ?? "",
                ProductPriceInCents = subscriptionResponse.Subscription.ProductPriceInCents ?? 0,
                State = subscriptionResponse.Subscription.State?.ToString() ?? "unknown",
                NextAssessmentAt = subscriptionResponse.Subscription.NextAssessmentAt?.DateTime,
                CurrentPeriodEndsAt = subscriptionResponse.Subscription.CurrentPeriodEndsAt?.DateTime
            };

            await repo.AddAsync(subscription);

            response.Success = true;
            response.SubscriptionId = subscription.MaxioSubscriptionId;
            response.State = subscription.State;
            response.ProductPriceInCents = (long)subscription.ProductPriceInCents;
            response.NextBillingDate = subscription.NextAssessmentAt;

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                return Results.BadRequest(new { errors = errorResponse.Errors });
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                return Results.StatusCode((int)raw.StatusCode);
            }
            return Results.BadRequest(new { error = "Failed to create subscription" });
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int)ex.Error.StatusCode);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetMySubscriptions(
        System.Security.Claims.ClaimsPrincipal user,
        Microsoft.eShopWeb.ApplicationCore.Interfaces.IReadRepository<Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription> repo)
    {
        var response = new GetMySubscriptionsResponse();
        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            // Note: Ideally this would use a specification to filter at the database level,
            // but due to the in-memory database, we filter in-memory for now
            var allSubscriptions = await repo.ListAsync();
            var userSubscriptions = allSubscriptions.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).ToList();

            response.Subscriptions = userSubscriptions
                .Select(s => new UserSubscriptionDto
                {
                    Id = s.Id,
                    MaxioSubscriptionId = s.MaxioSubscriptionId,
                    ProductName = s.ProductName,
                    ProductPriceInCents = (long)s.ProductPriceInCents,
                    State = s.State,
                    NextBillingDate = s.NextAssessmentAt,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    CreatedAt = s.CreatedAt
                })
                .ToList();

            response.Success = true;
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<int> EnsureCustomerExists(
        string userId,
        MaxioAdvancedBillingClient maxioClient)
    {
        try
        {
            var customerResponse = await maxioClient.Customers.ReadCustomerByReference(
                reference: userId,
                ct: default);

            if (customerResponse?.Customer != null)
            {
                return customerResponse.Customer.Id ?? 0;
            }
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                throw;
            }
        }

        var createCustomerRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new MaxioAdvancedBilling.Models.CreateCustomer
            {
                FirstName = "User",
                LastName = userId,
                Email = $"user-{userId}@eshopweb.local",
                Reference = userId
            }
        };

        var createResponse = await maxioClient.Customers.CreateCustomer(
            body: createCustomerRequest,
            ct: default);

        return createResponse?.Customer?.Id ?? 0;
    }
}

public class GetSubscriptionPlansResponse
{
    public bool Success { get; set; }
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
}

public class SubscribeRequest
{
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
}

public class CreateSubscriptionResponse
{
    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public long ProductPriceInCents { get; set; }
    public DateTime? NextBillingDate { get; set; }
}

public class GetMySubscriptionsResponse
{
    public bool Success { get; set; }
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductName { get; set; } = "";
    public long ProductPriceInCents { get; set; }
    public string State { get; set; } = "";
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
