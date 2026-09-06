using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IConfiguration config,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _logger = logger;
        _productFamilyHandle = config["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
    }

    public async Task<SubscriptionPlanDto[]> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();
            foreach (var productResponse in response)
            {
                var product = productResponse.Product;
                if (product.Id.HasValue)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id.Value,
                        Name = product.Name,
                        Handle = product.Handle,
                        Price = product.PriceInCents.HasValue ? product.PriceInCents.Value / 100m : 0m
                    });
                }
            }

            return plans.ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Error listing products from Maxio: {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to retrieve subscription plans.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing plans");
            throw;
        }
    }

    public async Task<int> EnsureCustomerExistsAsync(string userId, string? email = null, CancellationToken ct = default)
    {
        try
        {
            // Try to find existing customer by reference
            try
            {
                var existing = await _client.Customers.ReadCustomerByReference(reference: userId, ct: ct);
                if (existing.Customer.Id.HasValue)
                {
                    _logger.LogInformation("Found existing Maxio customer {CustomerId} for user {UserId}", existing.Customer.Id, userId);
                    return existing.Customer.Id.Value;
                }
                throw new InvalidOperationException($"Customer exists but has no ID for user {userId}");
            }
            catch (SdkException<RawError> ex)
            {
                if (ex.Error.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("Customer not found for reference {UserId}, creating new customer", userId);
                    // Customer doesn't exist, create one
                    var createRequest = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = "Customer",
                            LastName = userId,
                            Email = email ?? "",
                            Reference = userId
                        }
                    };

                    var newCustomer = await _client.Customers.CreateCustomer(body: createRequest, ct: ct);
                    if (newCustomer.Customer.Id.HasValue)
                    {
                        _logger.LogInformation("Created new Maxio customer {CustomerId} for user {UserId}", newCustomer.Customer.Id, userId);
                        return newCustomer.Customer.Id.Value;
                    }
                    throw new InvalidOperationException($"Failed to create customer - returned customer has no ID for user {userId}");

                }

                throw;
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validationError))
            {
                _logger.LogError("Validation error creating customer: {Message}", validationError?.ToString());
                throw new InvalidOperationException("Failed to create customer due to validation errors.", ex);
            }

            if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError("Error creating customer: {StatusCode} {Message}", rawError.StatusCode, rawError.ReadAsString());
                throw new InvalidOperationException("Failed to create customer.", ex);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error ensuring customer exists");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        int customerId,
        int productId,
        string? subscriptionReference = null,
        CancellationToken ct = default)
    {
        try
        {
            var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductId = productId,
                    PaymentCollectionMethod = CollectionMethod.Automatic,
                    Reference = subscriptionReference
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: request, ct: ct);
            var subscription = response.Subscription;

            if (!subscription.Id.HasValue)
            {
                throw new InvalidOperationException($"Failed to create subscription - returned subscription has no ID for customer {customerId}");
            }

            _logger.LogInformation(
                "Created subscription {SubscriptionId} for customer {CustomerId} on product {ProductId}",
                subscription.Id, customerId, productId);

            return new SubscriptionDto
            {
                Id = subscription.Id.Value,
                State = subscription.State?.Value ?? "unknown",
                Price = subscription.ProductPriceInCents.HasValue ? subscription.ProductPriceInCents.Value / 100m : 0m,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow,
                NextBillingAt = subscription.NextAssessmentAt ?? DateTimeOffset.UtcNow.AddMonths(1)
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationError))
            {
                _logger.LogError("Validation error creating subscription: {Message}", validationError?.ToString());
                throw new InvalidOperationException("Failed to create subscription due to validation errors.", ex);
            }

            if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError("Error creating subscription: {StatusCode} {Message}", rawError.StatusCode, rawError.ReadAsString());
                throw new InvalidOperationException("Failed to create subscription.", ex);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            throw;
        }
    }

    public async Task<SubscriptionDto[]> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);

            var subscriptions = new List<SubscriptionDto>();
            foreach (var subResponse in response)
            {
                var subscription = subResponse.Subscription;

                // Include active and trialing subscriptions
                if (subscription.State?.Value is "active" or "trialing" && subscription.Id.HasValue)
                {
                    subscriptions.Add(new SubscriptionDto
                    {
                        Id = subscription.Id.Value,
                        State = subscription.State.Value ?? "unknown",
                        Price = subscription.ProductPriceInCents.HasValue ? subscription.ProductPriceInCents.Value / 100m : 0m,
                        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow,
                        NextBillingAt = subscription.NextAssessmentAt ?? DateTimeOffset.UtcNow.AddMonths(1)
                    });
                }
            }

            _logger.LogInformation("Retrieved {Count} subscriptions for customer {CustomerId}", subscriptions.Count, customerId);
            return subscriptions.ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Error listing subscriptions for customer {CustomerId}: {StatusCode}", customerId, ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to retrieve subscriptions.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions");
            throw;
        }
    }
}
