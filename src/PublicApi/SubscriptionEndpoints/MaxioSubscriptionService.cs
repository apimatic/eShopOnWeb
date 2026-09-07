using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly Dictionary<string, int> _userCustomerIdMap = new();

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IConfiguration configuration,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _productFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
        _logger = logger;
    }

    public async Task<List<PlanDto>> GetAvailablePlansAsync()
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
                page: null,
                perPage: null,
                ct: default);

            var plans = new List<PlanDto>();
            foreach (var productResponse in response)
            {
                var product = productResponse.Product;
                plans.Add(new PlanDto
                {
                    Id = product?.Id ?? 0,
                    Name = product?.Name ?? string.Empty,
                    PriceInCents = product?.PriceInCents ?? 0,
                    Interval = product?.Interval ?? 1,
                    IntervalUnit = product?.IntervalUnit?.Value ?? "month",
                    Handle = product?.Handle ?? string.Empty
                });
            }

            return plans;
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError> ex)
        {
            _logger.LogError(ex, "Error listing products for product family");
            throw new InvalidOperationException("Failed to list subscription plans", ex);
        }
    }

    public async Task<SubscriptionResponseDto> CreateSubscriptionAsync(string userId, string productHandle)
    {
        try
        {
            int customerId = await GetOrCreateCustomerAsync(userId);

            var subscriptionRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    Reference = $"user-{userId}"
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: subscriptionRequest,
                ct: default);

            var subscription = response.Subscription;
            return new SubscriptionResponseDto
            {
                SubscriptionId = subscription?.Id ?? 0,
                CustomerId = subscription?.Customer?.Id ?? customerId,
                State = subscription?.State?.Value ?? "active",
                ActivatedAt = subscription?.ActivatedAt ?? DateTimeOffset.UtcNow
            };
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            if (!_userCustomerIdMap.TryGetValue(userId, out var customerId))
            {
                throw new InvalidOperationException($"No customer found for user {userId}");
            }

            var response = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: default);

            var subscriptions = new List<SubscriptionDto>();
            foreach (var subscriptionResponse in response)
            {
                var subscription = subscriptionResponse.Subscription;
                if (subscription?.State == SubscriptionState.Active)
                {
                    subscriptions.Add(new SubscriptionDto
                    {
                        Id = subscription.Id ?? 0,
                        State = subscription.State.Value,
                        ProductId = subscription.Product?.Id ?? 0,
                        ActivatedAt = subscription.ActivatedAt ?? DateTimeOffset.UtcNow,
                        NextAssessmentAt = subscription.CurrentPeriodEndsAt
                    });
                }
            }

            return subscriptions;
        }
        catch (SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex)
        {
            _logger.LogError(ex, "Error retrieving subscriptions for user {UserId}", userId);
            throw new InvalidOperationException("Failed to retrieve subscriptions", ex);
        }
    }

    private async Task<int> GetOrCreateCustomerAsync(string userId)
    {
        if (_userCustomerIdMap.TryGetValue(userId, out var existingCustomerId))
        {
            return existingCustomerId;
        }

        try
        {
            var readResponse = await _client.Customers.ReadCustomerByReference(
                reference: $"user-{userId}",
                ct: default);

            var customerId = readResponse.Customer?.Id;
            if (customerId.HasValue)
            {
                _userCustomerIdMap[userId] = customerId.Value;
                return customerId.Value;
            }
        }
        catch (SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex)
        {
            if ((int)ex.Error.StatusCode != 404)
            {
                _logger.LogError(ex, "Error reading customer for user {UserId}", userId);
                throw;
            }
        }

        var createRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new MaxioAdvancedBilling.Models.CreateCustomer
            {
                FirstName = "User",
                LastName = userId,
                Email = $"{userId}@example.com",
                Reference = $"user-{userId}"
            }
        };

        try
        {
            var createResponse = await _client.Customers.CreateCustomer(
                body: createRequest,
                ct: default);

            var newCustomerId = createResponse.Customer?.Id;
            if (newCustomerId.HasValue)
            {
                _userCustomerIdMap[userId] = newCustomerId.Value;
                return newCustomerId.Value;
            }

            throw new InvalidOperationException("Failed to get customer ID from create response");
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            _logger.LogError(ex, "Error creating customer for user {UserId}", userId);
            throw new InvalidOperationException("Failed to create customer", ex);
        }
    }
}
