using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IRepository<Subscription> subscriptionRepository,
        IOptions<MaxioSettings> settings)
    {
        _client = client;
        _subscriptionRepository = subscriptionRepository;
        _settings = settings.Value;
    }

    public async Task<List<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var plans = new List<SubscriptionPlanDto>();

        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyHandle,
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

            foreach (var productResponse in products)
            {
                var product = productResponse?.Product;
                if (product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Handle = product.Handle,
                        Name = product.Name,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 1,
                        IntervalUnit = product.IntervalUnit?.Value ?? "month"
                    });
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to list subscription plans: {ex.Error.StatusCode}", ex);
        }

        return plans;
    }

    public async Task<int> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        try
        {
            var existingCustomer = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);

            if (existingCustomer?.Customer?.Id.HasValue == true)
            {
                return existingCustomer.Customer.Id.Value;
            }
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"Failed to read customer: {ex.Error.StatusCode}", ex);
            }
        }

        var createRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new MaxioAdvancedBilling.Models.CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body: createRequest, ct: ct);
            if (response?.Customer?.Id.HasValue == true)
            {
                return response.Customer.Id.Value;
            }

            throw new InvalidOperationException("Failed to create customer: no ID in response");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                throw new InvalidOperationException($"Failed to create customer: {errorResponse}", ex);
            }
            throw new InvalidOperationException("Failed to create customer", ex);
        }
    }

    public async Task<SubscriptionResponseDto> CreateSubscriptionAsync(
        string userId,
        int customerId,
        string productHandle,
        CancellationToken ct = default)
    {
        var subscriptionRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = $"{userId}_{productHandle}_{DateTime.UtcNow.Ticks}"
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                body: subscriptionRequest,
                ct: ct);

            if (response?.Subscription == null)
            {
                throw new InvalidOperationException("Failed to create subscription: no subscription in response");
            }

            var subscription = response.Subscription;
            var priceInCents = subscription.BalanceInCents ?? 0;

            var subEntity = new Subscription(
                userId: userId,
                maxioCustomerId: customerId,
                maxioSubscriptionId: subscription.Id ?? 0,
                productHandle: productHandle,
                subscriptionState: subscription.State?.Value ?? "unknown",
                priceInCents: priceInCents,
                nextBillingAt: subscription.NextAssessmentAt);

            await _subscriptionRepository.AddAsync(subEntity);

            return new SubscriptionResponseDto
            {
                Id = subscription.Id ?? 0,
                State = subscription.State?.Value ?? "unknown",
                ProductHandle = productHandle,
                NextBillingAt = subscription.NextAssessmentAt,
                PriceInCents = priceInCents
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                throw new InvalidOperationException($"Failed to create subscription: {errorResponse}", ex);
            }
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
    }

    public async Task<List<SubscriptionResponseDto>> ListUserSubscriptionsAsync(
        int customerId,
        CancellationToken ct = default)
    {
        var subscriptions = new List<SubscriptionResponseDto>();

        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            foreach (var response in responses)
            {
                if (response?.Subscription != null)
                {
                    var sub = response.Subscription;
                    subscriptions.Add(new SubscriptionResponseDto
                    {
                        Id = sub.Id ?? 0,
                        State = sub.State?.Value ?? "unknown",
                        ProductHandle = sub.Product?.Handle ?? "unknown",
                        NextBillingAt = sub.NextAssessmentAt,
                        PriceInCents = sub.BalanceInCents ?? 0
                    });
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to list subscriptions: {ex.Error.StatusCode}", ex);
        }

        return subscriptions;
    }
}

public class SubscriptionPlanDto
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionResponseDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public decimal PriceInCents { get; set; }
}
