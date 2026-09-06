using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using SubscriptionEntity = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;
using MaxioCustomerMapping = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.MaxioCustomerMapping;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken ct = default);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IRepository<MaxioCustomerMapping> _customerMappingRepository;
    private readonly IRepository<SubscriptionEntity> _subscriptionRepository;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient maxioClient,
        IRepository<MaxioCustomerMapping> customerMappingRepository,
        IRepository<SubscriptionEntity> subscriptionRepository)
    {
        _maxioClient = maxioClient;
        _customerMappingRepository = customerMappingRepository;
        _subscriptionRepository = subscriptionRepository;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var products = await _maxioClient.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: (int?)1,
                perPage: (int?)100,
                ct: ct);

            var plans = products
                .Where(p => p.Product != null)
                .Select(p => new SubscriptionPlanDto
                {
                    Handle = p.Product!.Handle ?? "",
                    Name = p.Product.Name ?? "",
                    MonthlyPrice = (p.Product.PriceInCents ?? 0) / 100m,
                    IntervalMonths = (int?)(p.Product.Interval ?? 1)
                })
                .ToList();

            return plans;
        }
        catch
        {
            return new List<SubscriptionPlanDto>();
        }
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var customerId = await GetOrCreateMaxioCustomerAsync(userId, ct);

            var subscriptionRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var response = await _maxioClient.Subscriptions.CreateSubscription(subscriptionRequest, ct);

            if (response?.Subscription == null)
                return null;

            var subscription = response.Subscription;
            var dto = new SubscriptionDto
            {
                MaxioSubscriptionId = (int?)subscription.Id,
                ProductHandle = productHandle,
                State = subscription.State?.ToString() ?? "unknown",
                MonthlyPrice = (subscription.ProductPriceInCents ?? 0) / 100m,
                NextBillingDate = subscription.NextAssessmentAt
            };

            return dto;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var mappings = await _customerMappingRepository.ListAsync(ct);
            var userMapping = mappings.FirstOrDefault(m => m.UserId == userId);

            if (userMapping == null)
                return new List<SubscriptionDto>();

            var subscriptions = await _maxioClient.Subscriptions.ListSubscriptions(
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
                page: (int?)1,
                perPage: (int?)100,
                ct: ct);

            var userSubscriptions = subscriptions
                .Where(s => s.Subscription != null && s.Subscription.Customer?.Id == userMapping.MaxioCustomerId)
                .Select(s => new SubscriptionDto
                {
                    MaxioSubscriptionId = (int?)s.Subscription!.Id,
                    ProductHandle = s.Subscription.Product?.Handle ?? "",
                    State = s.Subscription.State?.ToString() ?? "unknown",
                    MonthlyPrice = (s.Subscription.ProductPriceInCents ?? 0L) / 100m,
                    NextBillingDate = s.Subscription.NextAssessmentAt
                })
                .ToList();

            return userSubscriptions;
        }
        catch
        {
            return new List<SubscriptionDto>();
        }
    }

    private async Task<int> GetOrCreateMaxioCustomerAsync(string userId, CancellationToken ct)
    {
        var mappings = await _customerMappingRepository.ListAsync(ct);
        var existing = mappings.FirstOrDefault(m => m.UserId == userId);

        if (existing != null)
            return existing.MaxioCustomerId;

        try
        {
            var customerResponse = await _maxioClient.Customers.ReadCustomerByReference(userId, ct);
            if (customerResponse?.Customer?.Id.HasValue == true)
            {
                var customerId = (int)customerResponse.Customer.Id.Value;
                var mapping = new MaxioCustomerMapping(userId, customerId);
                await _customerMappingRepository.AddAsync(mapping, ct);
                return customerId;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Customer doesn't exist, create one
        }

        var createRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new MaxioAdvancedBilling.Models.CreateCustomer
            {
                FirstName = "User",
                LastName = "Account",
                Email = userId,
                Reference = userId
            }
        };

        var createResponse = await _maxioClient.Customers.CreateCustomer(createRequest, ct);

        if (createResponse?.Customer?.Id.HasValue != true)
            throw new InvalidOperationException("Failed to create customer");

        var newCustomerId = (int)createResponse.Customer.Id.Value;
        var newMapping = new MaxioCustomerMapping(userId, newCustomerId);
        await _customerMappingRepository.AddAsync(newMapping, ct);

        return newCustomerId;
    }
}
