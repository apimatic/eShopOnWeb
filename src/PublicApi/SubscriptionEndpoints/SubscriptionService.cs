using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string planHandle, CancellationToken ct);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct)
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
                var product = productResponse.Product;
                if (product?.ProductFamily?.Handle == _options.ProductFamilyHandle && product.Id.HasValue)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Handle = product.Handle ?? string.Empty,
                        Name = product.Name ?? string.Empty,
                        Description = product.Description ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 1,
                        IntervalUnit = product.IntervalUnit?.Value ?? "month"
                    });
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list products");
            throw new SubscriptionException($"Failed to retrieve subscription plans: {ex.Error.ReadAsString()}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when listing products");
            throw new SubscriptionException("Failed to parse subscription plans response", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string planHandle, CancellationToken ct)
    {
        var customer = await GetOrCreateCustomerAsync(userId, ct);

        try
        {
            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                    DeferSignup = false
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(subscriptionRequest, ct: ct);
            var subscription = response.Subscription;

            return new SubscriptionDto
            {
                Id = subscription.Id ?? 0,
                ProductId = subscription.Product?.Id ?? 0,
                State = subscription.State?.Value ?? "unknown",
                CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var error422))
            {
                var errors = string.Join(", ", error422.Errors ?? Array.Empty<string>());
                _logger.LogError("Subscription creation failed with validation error: {Errors}", errors);
                throw new SubscriptionException($"Failed to create subscription: {errors}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError("Subscription creation failed with raw error: {Status}", rawError.StatusCode);
                throw new SubscriptionException($"Failed to create subscription: {rawError.ReadAsString()}", ex);
            }
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when creating subscription");
            throw new SubscriptionException("Failed to parse subscription creation response", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct)
    {
        var customer = await GetOrCreateCustomerAsync(userId, ct);
        var subscriptions = new List<SubscriptionDto>();

        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customer.Id ?? 0, ct: ct);

            foreach (var response in responses)
            {
                var subscription = response.Subscription;
                subscriptions.Add(new SubscriptionDto
                {
                    Id = subscription.Id ?? 0,
                    ProductId = subscription.Product?.Id ?? 0,
                    State = subscription.State?.Value ?? "unknown",
                    CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    CreatedAt = subscription.CreatedAt
                });
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list customer subscriptions");
            throw new SubscriptionException($"Failed to retrieve subscriptions: {ex.Error.ReadAsString()}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when listing subscriptions");
            throw new SubscriptionException("Failed to parse subscriptions response", ex);
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var existingCustomer = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
            if (existingCustomer.Customer?.Id.HasValue == true)
            {
                return existingCustomer.Customer;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Customer with reference {UserId} not found, creating new customer", userId);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON deserialization error when looking up customer, attempting creation");
        }

        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "User",
                    LastName = userId.Substring(0, Math.Min(10, userId.Length)),
                    Email = $"{userId}@eshop.local",
                    Reference = userId
                }
            };

            var createResponse = await _client.Customers.CreateCustomer(createRequest, ct: ct);
            return createResponse.Customer ?? throw new SubscriptionException("Customer creation returned no customer");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var error422))
            {
                _logger.LogInformation("Customer reference {UserId} already exists, retrieving existing customer", userId);
                var existingCustomer = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
                return existingCustomer.Customer ?? throw new SubscriptionException("Failed to retrieve existing customer");
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError("Customer creation failed: {Error}", rawError.ReadAsString());
                throw new SubscriptionException($"Failed to create customer: {rawError.ReadAsString()}", ex);
            }
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when creating customer");
            throw new SubscriptionException("Failed to parse customer creation response", ex);
        }
    }
}

public class SubscriptionException : Exception
{
    public SubscriptionException(string message) : base(message) { }
    public SubscriptionException(string message, Exception innerException) : base(message, innerException) { }
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
