using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly string _productFamilyHandle;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient maxioClient,
        string productFamilyHandle,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _productFamilyHandle = productFamilyHandle;
        _logger = logger;
    }

    public async Task<List<PlanModel>> GetAvailablePlansAsync(CancellationToken ct)
    {
        var plans = new List<PlanModel>();

        try
        {
            var response = await _maxioClient.ProductFamilies.ListProductsForProductFamily(
                _productFamilyHandle,
                null, null, null, null, null, null, null, null,
                page: 1, perPage: 20, ct);

            foreach (var productResponse in response)
            {
                if (productResponse?.Product != null)
                {
                    var product = productResponse.Product;
                    plans.Add(new PlanModel
                    {
                        Id = product.Id ?? 0,
                        Handle = product.Handle,
                        Name = product.Name,
                        Description = product.Description,
                        Price = product.PriceInCents.HasValue ? product.PriceInCents.Value / 100m : 0m,
                        Interval = product.IntervalUnit?.ToString(),
                        IntervalCount = product.Interval ?? 1
                    });
                }
            }

            _logger.LogInformation("Retrieved {PlanCount} plans from Maxio", plans.Count);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Error retrieving subscription plans from Maxio: {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to retrieve subscription plans", ex);
        }

        return plans;
    }

    public async Task<SubscriptionModel?> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken ct)
    {
        try
        {
            // Step 1: Look up or create customer
            var customer = await GetOrCreateCustomerAsync(userId, ct);
            if (customer?.Id == null)
            {
                throw new InvalidOperationException("Failed to create or retrieve customer");
            }

            // Step 2: Create subscription
            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = productHandle
                }
            };

            var response = await _maxioClient.Subscriptions.CreateSubscription(request, ct);

            if (response?.Subscription == null)
            {
                throw new InvalidOperationException("Failed to create subscription");
            }

            var subscription = response.Subscription;
            _logger.LogInformation(
                "Created subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}",
                subscription.Id, userId, productHandle);

            return new SubscriptionModel
            {
                Id = subscription.Id ?? 0,
                State = subscription.State?.Value,
                ProductHandle = subscription.Product?.Handle,
                ProductName = subscription.Product?.Name,
                Price = subscription.Product?.PriceInCents.HasValue == true
                    ? subscription.Product.PriceInCents.Value / 100m
                    : 0m,
                CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorBody))
            {
                var errorMessage = errorBody.Errors != null ? string.Join(", ", errorBody.Errors) : "Unknown error";
                _logger.LogWarning("Validation error creating subscription: {Errors}", errorMessage);
                throw new InvalidOperationException($"Subscription creation failed: {errorMessage}", ex);
            }

            _logger.LogWarning("Error creating subscription for user {UserId}", userId);
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Unexpected error creating subscription: {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
    }

    public async Task<List<SubscriptionModel>> GetUserSubscriptionsAsync(string userId, CancellationToken ct)
    {
        var subscriptions = new List<SubscriptionModel>();

        try
        {
            // Look up customer by userId reference
            var customer = await GetCustomerByReferenceAsync(userId, ct);
            if (customer?.Id == null)
            {
                _logger.LogInformation("No Maxio customer found for userId {UserId}", userId);
                return subscriptions;
            }

            // List customer subscriptions
            var response = await _maxioClient.Customers.ListCustomerSubscriptions(customer.Id.Value, ct);

            foreach (var subscriptionResponse in response)
            {
                if (subscriptionResponse?.Subscription != null)
                {
                    var sub = subscriptionResponse.Subscription;
                    subscriptions.Add(new SubscriptionModel
                    {
                        Id = sub.Id ?? 0,
                        State = sub.State?.Value,
                        ProductHandle = sub.Product?.Handle,
                        ProductName = sub.Product?.Name,
                        Price = sub.Product?.PriceInCents.HasValue == true
                            ? sub.Product.PriceInCents.Value / 100m
                            : 0m,
                        CurrentPeriodStartedAt = sub.CurrentPeriodStartedAt,
                        CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                        NextAssessmentAt = sub.NextAssessmentAt
                    });
                }
            }

            _logger.LogInformation("Retrieved {SubscriptionCount} subscriptions for user {UserId}", subscriptions.Count, userId);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Error retrieving subscriptions for user {UserId}: {StatusCode}", userId, ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to retrieve subscriptions", ex);
        }

        return subscriptions;
    }

    private async Task<Customer?> GetOrCreateCustomerAsync(string userId, CancellationToken ct)
    {
        // First, try to find existing customer by reference
        var existingCustomer = await GetCustomerByReferenceAsync(userId, ct);
        if (existingCustomer != null)
        {
            return existingCustomer;
        }

        // Create new customer
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = "eShop",
                LastName = "Customer",
                Email = $"{userId}@eshop.local",
                Reference = userId
            }
        };

        try
        {
            var response = await _maxioClient.Customers.CreateCustomer(request, ct);
            _logger.LogInformation("Created Maxio customer for user {UserId}", userId);
            return response?.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorBody))
            {
                _logger.LogWarning("Customer creation validation error: {Error}", errorBody.Errors);
                throw new InvalidOperationException($"Customer creation failed: {errorBody.Errors}", ex);
            }

            _logger.LogWarning("Error creating customer for user {UserId}", userId);
            throw;
        }
    }

    private async Task<Customer?> GetCustomerByReferenceAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await _maxioClient.Customers.ReadCustomerByReference(userId, ct);
            return response?.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            _logger.LogWarning("Error looking up customer by reference {UserId}: {StatusCode}", userId, ex.Error.StatusCode);
            throw;
        }
    }
}
