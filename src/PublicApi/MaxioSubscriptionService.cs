using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
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
                perPage: 100,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();
            foreach (var productResponse in response)
            {
                var product = productResponse.Product;
                if (product != null)
                {
                    plans.Add(new SubscriptionPlanDto(
                        Id: product.Id ?? 0,
                        Handle: product.Handle ?? string.Empty,
                        Name: product.Name ?? string.Empty,
                        Description: product.Description ?? string.Empty,
                        PriceInCents: product.PriceInCents ?? 0,
                        Interval: product.Interval ?? 1,
                        IntervalUnit: product.IntervalUnit?.Value ?? "month"));
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Error fetching subscription plans: {StatusCode}", ex.Error.StatusCode);
            throw;
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string planHandle, CancellationToken ct = default)
    {
        try
        {
            var customer = await EnsureMaxioCustomerExistsAsync(userId, ct);

            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                    Reference = userId,
                    PaymentCollectionMethod = CollectionMethod.Automatic
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: createRequest,
                ct: ct);

            var subscription = response.Subscription;
            if (subscription == null)
            {
                throw new InvalidOperationException("No subscription returned from Maxio");
            }

            return new SubscriptionDto(
                Id: subscription.Id ?? 0,
                State: subscription.State?.Value ?? "unknown",
                ProductHandle: planHandle,
                ProductPriceInCents: subscription.ProductPriceInCents ?? 0,
                NextBillingAt: subscription.NextAssessmentAt?.DateTime,
                ActivatedAt: subscription.ActivatedAt?.DateTime,
                Reference: subscription.Reference);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Error subscribing user {UserId} to plan {PlanHandle}: {StatusCode}",
                userId, planHandle, ex.Error.StatusCode);
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.ListSubscriptions(
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

            var subscriptions = new List<SubscriptionDto>();
            foreach (var subscriptionResponse in response)
            {
                var subscription = subscriptionResponse.Subscription;
                if (subscription?.Reference == userId)
                {
                    subscriptions.Add(new SubscriptionDto(
                        Id: subscription.Id ?? 0,
                        State: subscription.State?.Value ?? "unknown",
                        ProductHandle: subscription.Product?.Handle,
                        ProductPriceInCents: subscription.ProductPriceInCents ?? 0,
                        NextBillingAt: subscription.NextAssessmentAt?.DateTime,
                        ActivatedAt: subscription.ActivatedAt?.DateTime,
                        Reference: subscription.Reference));
                }
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Error fetching subscriptions for user {UserId}: {StatusCode}", userId, ex.Error.StatusCode);
            throw;
        }
    }

    private async Task<Customer> EnsureMaxioCustomerExistsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var existingResponse = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);
            var customer = existingResponse.Customer;
            return customer ?? throw new InvalidOperationException("Invalid customer response");
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "Customer",
                    LastName = userId,
                    Email = $"{userId}@example.local",
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: createRequest,
                ct: ct);

            var customer = response.Customer;
            return customer ?? throw new InvalidOperationException("Invalid customer response");
        }
    }
}
