using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionHandlers
{
    public static async Task<IResult> ListPlans(MaxioAdvancedBillingClient client)
    {
        try
        {
            var response = await client.Products.ListProducts(
                null, null, null, null, null, null, null, null,
                page: 1, perPage: 20, ct: default);

            var plans = new List<PlanDto>();
            foreach (var productResponse in response)
            {
                if (productResponse.Product != null)
                {
                    plans.Add(new PlanDto(
                        productResponse.Product.Id ?? 0,
                        productResponse.Product.Handle ?? string.Empty,
                        productResponse.Product.Name ?? string.Empty,
                        productResponse.Product.Description ?? string.Empty,
                        (productResponse.Product.PriceInCents ?? 0) / 100m,
                        productResponse.Product.Interval ?? 1,
                        productResponse.Product.IntervalUnit?.ToString() ?? "month"));
                }
            }

            return Results.Ok(new { plans });
        }
        catch (SdkException<RawError> ex)
        {
            var statusCode = ex.Error.StatusCode;
            int code = statusCode != null ? (int)statusCode : 500;
            return Results.StatusCode(code);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    public static async Task<IResult> CreateSubscription(
        CreateSubscriptionDto request,
        HttpContext httpContext,
        MaxioAdvancedBillingClient client,
        IRepository<Microsoft.eShopWeb.ApplicationCore.Entities.Subscription> subscriptionRepo)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.BadRequest(new { message = "User identity not found" });

        try
        {
            // Step 2a: Try to get existing customer
            CustomerResponse? existingCustomer = null;
            try
            {
                var customerResp = await client.Customers.ReadCustomerByReference(reference: userId, ct: default);
                existingCustomer = customerResp;
            }
            catch (SdkException<RawError> ex)
            {
                if (ex.Error.StatusCode != System.Net.HttpStatusCode.NotFound)
                    return Results.StatusCode((int)ex.Error.StatusCode);
            }

            // Step 2b: Create customer if doesn't exist
            int maxioCustomerId;
            if (existingCustomer?.Customer != null)
            {
                maxioCustomerId = existingCustomer.Customer.Id ?? 0;
            }
            else
            {
                try
                {
                    var createCustomerResp = await client.Customers.CreateCustomer(
                        body: new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = "User",
                                LastName = userId,
                                Email = $"{userId}@eshop.local",
                                Reference = userId
                            }
                        },
                        ct: default);

                    maxioCustomerId = createCustomerResp.Customer?.Id ?? 0;
                    if (maxioCustomerId == 0)
                        return Results.BadRequest(new { message = "Failed to create customer" });
                }
                catch (SdkException<CreateCustomerError> ex)
                {
                    if (ex.Error.TryGetCustomerErrorResponse1(out var errResp))
                    {
                        try
                        {
                            var customerResp = await client.Customers.ReadCustomerByReference(reference: userId, ct: default);
                            maxioCustomerId = customerResp.Customer?.Id ?? 0;
                        }
                        catch (SdkException<RawError>)
                        {
                            return Results.BadRequest(new { message = "Customer creation failed" });
                        }
                    }
                    else if (ex.Error.TryGetRawError(out RawError raw))
                    {
                        return Results.StatusCode((int)raw.StatusCode);
                    }
                    else
                    {
                        return Results.BadRequest(new { message = "Customer creation failed" });
                    }
                }
            }

            // Step 2c: Create subscription
            var subscriptionReference = $"{userId}-{request.PlanHandle}";
            int maxioSubscriptionId;
            try
            {
                var createSubResp = await client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            CustomerId = maxioCustomerId,
                            ProductHandle = request.PlanHandle,
                            Reference = subscriptionReference,
                            DeferSignup = false
                        }
                    },
                    ct: default);

                maxioSubscriptionId = createSubResp.Subscription?.Id ?? 0;
                if (maxioSubscriptionId == 0)
                    return Results.BadRequest(new { message = "Failed to create subscription" });
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errList))
                {
                    var errorMsg = string.Join(", ", errList.Errors ?? new List<string>());
                    if (errorMsg.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                        return Results.Conflict(new { message = "Subscription already exists" });
                    return Results.BadRequest(new { message = errorMsg });
                }
                else if (ex.Error.TryGetRawError(out RawError raw))
                {
                    return Results.StatusCode((int)raw.StatusCode);
                }
                else
                {
                    return Results.BadRequest(new { message = "Subscription creation failed" });
                }
            }

            // Store subscription metadata locally
            var subscription = new Microsoft.eShopWeb.ApplicationCore.Entities.Subscription(
                userId,
                maxioCustomerId,
                maxioSubscriptionId,
                subscriptionReference,
                request.PlanHandle,
                "active",
                null,
                DateTimeOffset.UtcNow);

            await subscriptionRepo.AddAsync(subscription);
            await subscriptionRepo.SaveChangesAsync();

            return Results.Created(
                $"/api/subscriptions/{maxioSubscriptionId}",
                new
                {
                    id = maxioSubscriptionId,
                    productHandle = request.PlanHandle,
                    state = "active",
                    nextBillingAt = (DateTimeOffset?)null,
                    createdAt = DateTimeOffset.UtcNow
                });
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    public static async Task<IResult> GetMySubscriptions(
        HttpContext httpContext,
        MaxioAdvancedBillingClient client)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.BadRequest(new { message = "User identity not found" });

        try
        {
            // Step 3a: Get customer ID by reference
            int? customerId = null;
            try
            {
                var customerResp = await client.Customers.ReadCustomerByReference(reference: userId, ct: default);
                customerId = customerResp.Customer?.Id;
            }
            catch (SdkException<RawError> ex)
            {
                var statusCode = ex.Error.StatusCode;
                if (statusCode != System.Net.HttpStatusCode.NotFound)
                    return Results.StatusCode((int)statusCode);
            }

            // If no customer, return empty list
            if (customerId == null || customerId == 0)
                return Results.Ok(new { subscriptions = new List<SubscriptionDto>() });

            // Step 3b: List subscriptions for customer
            var response = await client.Customers.ListCustomerSubscriptions(customerId: customerId.Value, ct: default);

            var subscriptions = new List<SubscriptionDto>();
            foreach (var subResp in response)
            {
                if (subResp.Subscription != null)
                {
                    subscriptions.Add(new SubscriptionDto(
                        subResp.Subscription.Id ?? 0,
                        subResp.Subscription.Product?.Handle ?? string.Empty,
                        subResp.Subscription.State?.ToString() ?? "unknown",
                        subResp.Subscription.NextAssessmentAt,
                        subResp.Subscription.CreatedAt ?? DateTimeOffset.UtcNow));
                }
            }

            return Results.Ok(new { subscriptions });
        }
        catch (SdkException<RawError> ex)
        {
            var statusCode = ex.Error.StatusCode;
            int code = statusCode != null ? (int)statusCode : 500;
            return Results.StatusCode(code);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class CreateSubscriptionDto
{
    public string PlanHandle { get; set; } = string.Empty;
}

public record PlanDto(
    int Id,
    string Handle,
    string Name,
    string Description,
    decimal PricePerMonth,
    int Interval,
    string IntervalUnit);

public record SubscriptionDto(
    int Id,
    string ProductHandle,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset CreatedAt);
