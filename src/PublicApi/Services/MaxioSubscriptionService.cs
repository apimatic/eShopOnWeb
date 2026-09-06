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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<SubscriptionPlanDto?> GetPlanByHandleAsync(string handle, CancellationToken ct = default);
    Task<int> EnsureCustomerExistsAsync(string userId, string firstName, string lastName, string email, CancellationToken ct = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default);
    Task<List<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
    Task<SubscriptionDto?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioSettings settings, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var products = await _client.Products.ListProducts(
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
            foreach (var productResponse in products)
            {
                if (productResponse.Product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = productResponse.Product.Id ?? 0,
                        Name = productResponse.Product.Name ?? string.Empty,
                        Handle = productResponse.Product.Handle ?? string.Empty,
                        PriceInCents = productResponse.Product.PriceInCents ?? 0,
                        Interval = productResponse.Product.Interval ?? 0,
                        IntervalUnit = productResponse.Product.IntervalUnit?.Value ?? string.Empty
                    });
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to list plans: {StatusCode} - {Message}",
                (int)ex.Error.StatusCode, ex.Error.ReadAsString());
            throw;
        }
    }

    public async Task<SubscriptionPlanDto?> GetPlanByHandleAsync(string handle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(handle, ct: ct);

            if (response?.Product == null)
                return null;

            return new SubscriptionPlanDto
            {
                Id = response.Product.Id ?? 0,
                Name = response.Product.Name ?? string.Empty,
                Handle = response.Product.Handle ?? string.Empty,
                PriceInCents = response.Product.PriceInCents ?? 0,
                Interval = response.Product.Interval ?? 0,
                IntervalUnit = response.Product.IntervalUnit?.Value ?? string.Empty
            };
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to get plan {Handle}: {StatusCode} - {Message}",
                handle, (int)ex.Error.StatusCode, ex.Error.ReadAsString());
            throw;
        }
    }

    public async Task<int> EnsureCustomerExistsAsync(string userId, string firstName, string lastName, string email, CancellationToken ct = default)
    {
        try
        {
            // Check if customer already exists by reference
            try
            {
                var existing = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
                if (existing?.Customer?.Id != null)
                {
                    _logger.LogInformation("Customer already exists for user {UserId}: {CustomerId}", userId, existing.Customer.Id);
                    return existing.Customer.Id.Value;
                }
            }
            catch (SdkException<RawError> ex)
            {
                if ((int)ex.Error.StatusCode != 404)
                    throw;
            }

            // Create new customer
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(createRequest, ct: ct);

            if (response?.Customer?.Id == null)
                throw new InvalidOperationException("Failed to create customer");

            _logger.LogInformation("Created customer {CustomerId} for user {UserId}", response.Customer.Id, userId);
            return response.Customer.Id.Value;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var customerError))
            {
                _logger.LogError("Customer creation failed with validation error: {Error}", customerError);
                throw new InvalidOperationException("Customer validation failed", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError("Customer creation failed: {StatusCode} - {Message}",
                    (int)rawError.StatusCode, rawError.ReadAsString());
                throw new InvalidOperationException("Customer creation failed", ex);
            }
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    PaymentCollectionMethod = CollectionMethod.Automatic
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(createRequest, ct: ct);

            if (response?.Subscription == null)
                throw new InvalidOperationException("Failed to create subscription");

            return MapSubscriptionResponse(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                _logger.LogError("Subscription creation failed with validation error: {Error}", errorList);
                throw new InvalidOperationException("Subscription validation failed", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError("Subscription creation failed: {StatusCode} - {Message}",
                    (int)rawError.StatusCode, rawError.ReadAsString());
                throw new InvalidOperationException("Subscription creation failed", ex);
            }
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);

            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => MapSubscriptionResponse(s.Subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to list customer subscriptions: {StatusCode} - {Message}",
                (int)ex.Error.StatusCode, ex.Error.ReadAsString());
            throw;
        }
    }

    public async Task<SubscriptionDto?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, null, ct: ct);

            if (response?.Subscription == null)
                return null;

            return MapSubscriptionResponse(response.Subscription);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to get subscription {SubscriptionId}: {StatusCode} - {Message}",
                subscriptionId, (int)ex.Error.StatusCode, ex.Error.ReadAsString());
            throw;
        }
    }

    private static SubscriptionDto MapSubscriptionResponse(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            CustomerId = subscription.Customer?.Id ?? 0,
            State = subscription.State?.Value ?? string.Empty,
            ProductPriceInCents = subscription.ProductPriceInCents ?? 0,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt,
            ProductName = subscription.Product?.Name ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty
        };
    }
}
