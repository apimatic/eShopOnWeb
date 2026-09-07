using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly string? _productFamilyHandle;
    private const string CustomerCacheKeyPrefix = "maxio_customer_";

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger,
        IConfiguration configuration)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
        _productFamilyHandle = configuration["Maxio:ProductFamilyHandle"];
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct)
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
                if (productResponse?.Product == null)
                    continue;

                var product = productResponse.Product;

                if (product.ProductFamily?.Handle != _productFamilyHandle)
                    continue;

                plans.Add(new SubscriptionPlanDto
                {
                    Id = product.Id ?? 0,
                    Name = product.Name ?? string.Empty,
                    Handle = product.Handle ?? string.Empty,
                    PriceInCents = product.PriceInCents ?? 0,
                    BillingInterval = product.Interval ?? 0,
                    IntervalUnit = product.IntervalUnit?.ToString() ?? "month"
                });
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio. Status: {StatusCode}",
                (int)ex.Error.StatusCode);
            throw new MaxioServiceException("Failed to fetch subscription plans", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when fetching subscription plans");
            throw new MaxioServiceException("Failed to deserialize subscription plans response", ex);
        }
    }

    public async Task<int> EnsureMaxioCustomerAsync(string userId, string email, CancellationToken ct)
    {
        var cacheKey = $"{CustomerCacheKeyPrefix}{userId}";

        if (_cache.TryGetValue(cacheKey, out int cachedCustomerId))
        {
            _logger.LogDebug("Found cached Maxio customer ID {CustomerId} for user {UserId}", cachedCustomerId, userId);
            return cachedCustomerId;
        }

        try
        {
            var existingCustomer = await TryGetCustomerByReferenceAsync(email, ct);
            if (existingCustomer != null)
            {
                _logger.LogInformation("Found existing Maxio customer {CustomerId} for user {UserId}", existingCustomer.Id, userId);
                CacheCustomerId(cacheKey, existingCustomer.Id ?? 0);
                return existingCustomer.Id ?? 0;
            }

            _logger.LogInformation("Creating new Maxio customer for user {UserId} with email {Email}", userId, email);
            var newCustomer = await CreateMaxioCustomerAsync(email, ct);
            CacheCustomerId(cacheKey, newCustomer.Id ?? 0);
            return newCustomer.Id ?? 0;
        }
        catch (MaxioServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error ensuring Maxio customer for user {UserId}", userId);
            throw new MaxioServiceException("Failed to ensure Maxio customer exists", ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> TryGetCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: reference,
                ct: ct);

            return response?.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Customer with reference {Reference} not found", reference);
                return null;
            }

            _logger.LogError(ex, "Error reading customer by reference {Reference}. Status: {StatusCode}",
                reference, (int)ex.Error.StatusCode);
            throw new MaxioServiceException($"Failed to lookup customer: {ex.Error.ReadAsString()}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when reading customer by reference");
            throw new MaxioServiceException("Failed to deserialize customer response", ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Customer> CreateMaxioCustomerAsync(string email, CancellationToken ct)
    {
        try
        {
            var nameParts = email.Split('@')[0].Split('.');
            var firstName = nameParts.Length > 0 ? nameParts[0] : "Customer";
            var lastName = nameParts.Length > 1 ? nameParts[1] : "User";

            var request = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = email
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: request,
                ct: ct);

            if (response?.Customer == null)
                throw new MaxioServiceException("CreateCustomer returned null customer");

            return response.Customer;
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validationError))
            {
                var errorMsg = validationError.Errors != null
                    ? string.Join(", ", validationError.Errors)
                    : "Validation error";
                _logger.LogError("Customer creation validation failed: {Error}", errorMsg);
                throw new MaxioServiceException($"Validation failed: {errorMsg}", ex);
            }

            if (ex.Error.TryGetRawError(out RawError rawError))
            {
                _logger.LogError("Customer creation failed with status {StatusCode}: {Response}",
                    (int)rawError.StatusCode, rawError.ReadAsString());
                throw new MaxioServiceException($"Failed to create customer: {rawError.ReadAsString()}", ex);
            }

            throw new MaxioServiceException("Failed to create customer", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON error during customer creation");
            throw new MaxioServiceException("Failed to deserialize customer creation response", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        int maxioCustomerId,
        string userEmail,
        string productHandle,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Creating subscription for customer {CustomerId} on product {ProductHandle}",
                maxioCustomerId, productHandle);

            var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = maxioCustomerId,
                    ReceivesInvoiceEmails = "true",
                    Reference = userEmail
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: request,
                ct: ct);

            if (response?.Subscription == null)
                throw new MaxioServiceException("CreateSubscription returned null subscription");

            return MapToSubscriptionDto(response.Subscription);
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationError))
            {
                var errorMsg = validationError.Errors != null && validationError.Errors.Count > 0
                    ? string.Join(", ", validationError.Errors)
                    : "Validation error";
                _logger.LogError("Subscription creation validation failed: {Error}", errorMsg);
                throw new MaxioServiceException($"Validation failed: {errorMsg}", ex);
            }

            if (ex.Error.TryGetRawError(out RawError rawError))
            {
                _logger.LogError("Subscription creation failed with status {StatusCode}: {Response}",
                    (int)rawError.StatusCode, rawError.ReadAsString());
                throw new MaxioServiceException($"Failed to create subscription: {rawError.ReadAsString()}", ex);
            }

            throw new MaxioServiceException("Failed to create subscription", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON error during subscription creation");
            throw new MaxioServiceException("Failed to deserialize subscription response", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(int maxioCustomerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                customerId: maxioCustomerId,
                ct: ct);

            var subscriptions = new List<SubscriptionDto>();

            foreach (var subResponse in response)
            {
                if (subResponse?.Subscription == null)
                    continue;

                var sub = subResponse.Subscription;

                if (sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing)
                {
                    subscriptions.Add(MapToSubscriptionDto(sub));
                }
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for customer {CustomerId}. Status: {StatusCode}",
                maxioCustomerId, (int)ex.Error.StatusCode);
            throw new MaxioServiceException("Failed to fetch subscriptions", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when fetching subscriptions");
            throw new MaxioServiceException("Failed to deserialize subscriptions response", ex);
        }
    }

    private SubscriptionDto MapToSubscriptionDto(MaxioAdvancedBilling.Models.Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            ProductName = subscription.Product?.Name ?? "Unknown",
            State = subscription.State?.Value ?? "unknown",
            BalanceInCents = subscription.BalanceInCents ?? 0,
            PriceInCents = subscription.ProductPriceInCents ?? 0,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt
        };
    }

    private void CacheCustomerId(string cacheKey, int customerId)
    {
        _cache.Set(cacheKey, customerId, TimeSpan.FromHours(24));
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int BillingInterval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}

public class MaxioServiceException : Exception
{
    public MaxioServiceException(string message) : base(message) { }
    public MaxioServiceException(string message, Exception innerException) : base(message, innerException) { }
}
