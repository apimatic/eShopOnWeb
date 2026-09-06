using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private static readonly ConcurrentDictionary<string, int> UserCustomerMap = new();

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{_settings.ProductFamilyHandle}",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 50,
                ct: ct);

            var plans = new List<SubscriptionPlan>();
            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                if (product?.Id.HasValue == true && !string.IsNullOrEmpty(product.Handle))
                {
                    plans.Add(new SubscriptionPlan
                    {
                        Id = product.Id.Value,
                        Handle = product.Handle,
                        Name = product.Name ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        IntervalDays = product.Interval ?? 30
                    });
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to fetch subscription plans: {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to fetch subscription plans", ex);
        }
    }

    public async Task<SubscriptionInfo> CreateSubscriptionAsync(string userId, string userEmail, string planHandle, CancellationToken ct = default)
    {
        try
        {
            int customerId = await EnsureCustomerExistsAsync(userId, userEmail, ct);

            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = planHandle,
                    Reference = userId
                }
            };

            var subscriptionResponse = await _client.Subscriptions.CreateSubscription(subscriptionRequest, ct);
            var subscription = subscriptionResponse.Subscription;

            if (subscription?.Id.HasValue != true)
                throw new InvalidOperationException("Subscription creation failed: missing subscription ID");

            return new SubscriptionInfo
            {
                Id = subscription.Id.Value,
                State = subscription.State?.Value ?? "unknown",
                ProductHandle = planHandle,
                BalanceInCents = subscription.BalanceInCents ?? 0,
                NextBillingDate = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt ?? DateTimeOffset.UtcNow
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError("Failed to create subscription: {Error}", ex.Error);
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to create subscription: {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            if (!UserCustomerMap.TryGetValue(userId, out int customerId))
            {
                return new List<SubscriptionInfo>();
            }

            var subscriptions = await _client.Subscriptions.ListSubscriptions(
                state: null,
                product: null,
                productPricePointId: null,
                coupon: null,
                couponCode: null,
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                metadata: null,
                direction: null,
                sort: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            var userSubs = new List<SubscriptionInfo>();
            foreach (var subscriptionResponse in subscriptions)
            {
                var subscription = subscriptionResponse.Subscription;
                if (subscription?.Customer?.Id == customerId && subscription.Id.HasValue)
                {
                    userSubs.Add(new SubscriptionInfo
                    {
                        Id = subscription.Id.Value,
                        State = subscription.State?.Value ?? "unknown",
                        ProductHandle = subscription.Product?.Handle ?? string.Empty,
                        BalanceInCents = subscription.BalanceInCents ?? 0,
                        NextBillingDate = subscription.NextAssessmentAt,
                        CreatedAt = subscription.CreatedAt ?? DateTimeOffset.UtcNow
                    });
                }
            }

            return userSubs;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to fetch subscriptions: {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to fetch subscriptions", ex);
        }
    }

    private async Task<int> EnsureCustomerExistsAsync(string userId, string userEmail, CancellationToken ct)
    {
        if (UserCustomerMap.TryGetValue(userId, out int existingCustomerId))
        {
            return existingCustomerId;
        }

        try
        {
            var existingCustomers = await _client.Customers.ListCustomers(
                direction: null,
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                q: userEmail,
                page: 1,
                perPage: 10,
                ct: ct);

            foreach (var customerResponse in existingCustomers)
            {
                var customer = customerResponse.Customer;
                if (customer?.Email == userEmail && customer.Id.HasValue)
                {
                    UserCustomerMap.TryAdd(userId, customer.Id.Value);
                    return customer.Id.Value;
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Failed to search for existing customer: {StatusCode}", ex.Error.StatusCode);
        }

        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = userId.Split('@')[0],
                LastName = "Customer",
                Email = userEmail,
                Reference = userId
            }
        };

        try
        {
            var customerResponse = await _client.Customers.CreateCustomer(createRequest, ct);
            var customer = customerResponse.Customer;

            if (customer?.Id.HasValue != true)
                throw new InvalidOperationException("Customer creation failed: missing customer ID");

            UserCustomerMap.TryAdd(userId, customer.Id.Value);
            return customer.Id.Value;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            _logger.LogError("Failed to create customer: {Error}", ex.Error);
            throw new InvalidOperationException("Failed to create or find customer", ex);
        }
    }
}
