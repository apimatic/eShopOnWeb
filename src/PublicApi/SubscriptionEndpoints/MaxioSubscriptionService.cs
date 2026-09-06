using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        string productFamilyHandle,
        UserManager<ApplicationUser> userManager,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _productFamilyHandle = productFamilyHandle;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _productFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();
            foreach (var productResponse in response)
            {
                var product = productResponse.Product;
                if (product == null) continue;

                var plan = new SubscriptionPlanDto
                {
                    Id = product.Id ?? 0,
                    Handle = product.Handle ?? string.Empty,
                    Name = product.Name ?? string.Empty,
                    PriceInDollars = (product.PriceInCents ?? 0) / 100m,
                    BillingIntervalDays = product.Interval ?? 30,
                    BillingIntervalUnit = product.IntervalUnit?.Value ?? "month"
                };
                plans.Add(plan);
            }

            return plans;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError($"Failed to list products: HTTP {(int)raw.StatusCode}");
            }
            throw new InvalidOperationException("Failed to retrieve subscription plans", ex);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "JSON deserialization error listing products");
            throw new InvalidOperationException("Provider returned unexpected response format", jsonEx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving subscription plans");
            throw;
        }
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(
        ApplicationUser user,
        string productHandle,
        CancellationToken ct = default)
    {
        try
        {
            // Step 1: Ensure customer exists (idempotent)
            var customerId = await EnsureCustomerExistsAsync(user, ct);

            // Step 2: Create subscription
            var subscriptionReference = $"sub-{user.Id}-{Guid.NewGuid()}";

            var sdkCreateSubRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    CustomerReference = user.Id,
                    ProductHandle = productHandle,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = CollectionMethod.Automatic
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: sdkCreateSubRequest,
                ct: ct);

            return response?.Subscription != null
                ? MapSubscriptionToDto(response.Subscription)
                : null;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors422))
            {
                var errorMessages = errors422.Errors ?? new List<string>();
                _logger.LogWarning($"Subscription creation validation error: {string.Join("; ", errorMessages)}");
                throw new InvalidOperationException("Failed to create subscription: " + string.Join("; ", errorMessages), ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError($"Subscription creation error: HTTP {raw.StatusCode}");
            }
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "JSON deserialization error creating subscription");
            throw new InvalidOperationException("Provider returned unexpected response format", jsonEx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(
        ApplicationUser user,
        CancellationToken ct = default)
    {
        try
        {
            // First, lookup the customer by reference (user ID)
            var customerResponse = await TryGetCustomerAsync(user.Id, ct);
            if (customerResponse?.Customer == null)
            {
                return new List<SubscriptionDto>();
            }

            var customerId = customerResponse.Customer.Id ?? 0;
            if (customerId == 0)
            {
                return new List<SubscriptionDto>();
            }

            // Then list subscriptions for that customer
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            var result = new List<SubscriptionDto>();
            foreach (var subResponse in subscriptions)
            {
                if (subResponse.Subscription != null)
                {
                    result.Add(MapSubscriptionToDto(subResponse.Subscription));
                }
            }
            return result;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return new List<SubscriptionDto>();
            }
            _logger.LogError($"Failed to list subscriptions: HTTP {ex.Error.StatusCode}");
            throw new InvalidOperationException("Failed to retrieve subscriptions", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving subscriptions");
            throw;
        }
    }

    private async Task<int> EnsureCustomerExistsAsync(ApplicationUser user, CancellationToken ct)
    {
        // Try to find existing customer
        var existing = await TryGetCustomerAsync(user.Id, ct);
        if (existing?.Customer != null)
        {
            return existing.Customer.Id ?? 0;
        }

        // Create new customer
        try
        {
            var sdkCreateRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                {
                    FirstName = user.UserName?.Split('@')[0] ?? "User",
                    LastName = string.Empty,
                    Email = user.Email ?? user.UserName ?? string.Empty,
                    Reference = user.Id
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: sdkCreateRequest,
                ct: ct);

            return response?.Customer?.Id ?? 0;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Check if it's a duplicate reference error
            if (ex.Error.TryGetCustomerErrorResponse1(out var errors422))
            {
                _logger.LogWarning("Customer creation returned 422: possible duplicate reference. Retrying lookup.");
                var retryLookup = await TryGetCustomerAsync(user.Id, ct);
                if (retryLookup?.Customer != null)
                {
                    return retryLookup.Customer.Id ?? 0;
                }
                throw new InvalidOperationException("Customer creation conflict unresolved", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError($"Customer creation error: HTTP {raw.StatusCode}");
            }
            throw new InvalidOperationException("Failed to create customer", ex);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "JSON deserialization error creating customer");
            throw new InvalidOperationException("Provider returned unexpected response format", jsonEx);
        }
    }

    private async Task<CustomerResponse?> TryGetCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ReadCustomerByReference(
                reference: reference,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw;
        }
    }

    private SubscriptionDto MapSubscriptionToDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.Value ?? string.Empty,
            ProductId = subscription.Product?.Id ?? 0,
            ProductHandle = subscription.Product?.Handle,
            CustomerId = subscription.Customer?.Id ?? 0,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CreatedAt = subscription.CreatedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = subscription.UpdatedAt ?? DateTimeOffset.UtcNow,
            Reference = subscription.Reference
        };
    }
}
