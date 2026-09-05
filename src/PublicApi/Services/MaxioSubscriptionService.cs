using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<MaxioCustomerDto> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(int customerId, CancellationToken ct);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IConfiguration configuration,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = configuration.GetSection("Maxio").Get<MaxioSettings>() ?? new MaxioSettings();
        _logger = logger;
    }

    public async Task<MaxioCustomerDto> GetOrCreateCustomerAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Checking for existing Maxio customer with reference {UserId}", userId);
            var existingCustomer = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
            if (existingCustomer?.Customer != null)
            {
                _logger.LogInformation("Found existing Maxio customer {CustomerId} for user {UserId}", existingCustomer.Customer.Id, userId);
                return new MaxioCustomerDto
                {
                    Id = existingCustomer.Customer.Id ?? 0,
                    Email = existingCustomer.Customer.Email,
                    Reference = existingCustomer.Customer.Reference
                };
            }
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("No existing customer found for reference {UserId}", userId);
            }
            else
            {
                _logger.LogError(ex, "Error reading customer by reference {UserId}: {StatusCode}", userId, ex.Error.StatusCode);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error reading customer by reference {UserId}", userId);
            throw;
        }

        try
        {
            _logger.LogInformation("Creating new Maxio customer for user {UserId}", userId);
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

            var response = await _client.Customers.CreateCustomer(body: createRequest, ct: ct);
            if (response?.Customer != null)
            {
                _logger.LogInformation("Successfully created Maxio customer {CustomerId} for user {UserId}", response.Customer.Id, userId);
                return new MaxioCustomerDto
                {
                    Id = response.Customer.Id ?? 0,
                    Email = response.Customer.Email,
                    Reference = response.Customer.Reference
                };
            }

            throw new InvalidOperationException("Customer creation succeeded but returned no customer data");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                _logger.LogError("Customer creation failed with validation error: {Errors}", string.Join("; ", errorResponse.Errors ?? new List<string>()));
                throw new InvalidOperationException($"Failed to create customer: {string.Join("; ", errorResponse.Errors ?? new List<string>())}");
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError(ex, "Customer creation failed: HTTP {StatusCode}", raw.StatusCode);
                throw;
            }
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize customer creation response");
            throw new InvalidOperationException("Provider returned an unparseable response", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Listing subscription plans");
            var products = await _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();
            foreach (var productResponse in products)
            {
                if (productResponse.Product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = productResponse.Product.Id ?? 0,
                        Handle = productResponse.Product.Handle,
                        Name = productResponse.Product.Name,
                        Description = productResponse.Product.Description,
                        PriceInCents = productResponse.Product.PriceInCents ?? 0,
                        Interval = productResponse.Product.Interval ?? 1,
                        IntervalUnit = productResponse.Product.IntervalUnit?.ToString() ?? "Month"
                    });
                }
            }

            _logger.LogInformation("Retrieved {PlanCount} subscription plans", plans.Count);
            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing products: HTTP {StatusCode}", ex.Error.StatusCode);
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize products response");
            throw new InvalidOperationException("Provider returned an unparseable response", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Checking for existing active subscription for customer {CustomerId} with product {ProductHandle}", customerId, productHandle);

            var existingSubscriptions = await _client.Subscriptions.ListSubscriptions(
                state: SubscriptionStateFilter.Active,
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

            foreach (var subResponse in existingSubscriptions)
            {
                var sub = subResponse.Subscription;
                if (sub?.CustomerId == customerId && sub.ProductHandle == productHandle && sub.State == SubscriptionState.Active)
                {
                    _logger.LogInformation("Found existing active subscription {SubscriptionId} for customer {CustomerId}", sub.Id, customerId);
                    return MapSubscriptionDto(sub);
                }
            }

            _logger.LogInformation("Creating new subscription for customer {CustomerId} with product {ProductHandle}", customerId, productHandle);
            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: createRequest, ct: ct);
            if (response?.Subscription != null)
            {
                _logger.LogInformation("Successfully created subscription {SubscriptionId} for customer {CustomerId}", response.Subscription.Id, customerId);
                return MapSubscriptionDto(response.Subscription);
            }

            throw new InvalidOperationException("Subscription creation succeeded but returned no subscription data");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                var errors = errorResponse.Errors ?? new List<string>();
                _logger.LogError("Subscription creation failed with validation error: {Errors}", string.Join("; ", errors));
                throw new InvalidOperationException($"Failed to create subscription: {string.Join("; ", errors)}");
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError(ex, "Subscription creation failed: HTTP {StatusCode}", raw.StatusCode);
                throw;
            }
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize subscription creation response");
            throw new InvalidOperationException("Provider returned an unparseable response", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
        int customerId,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Listing subscriptions for customer {CustomerId}", customerId);

            var subscriptions = await _client.Subscriptions.ListSubscriptions(
                state: SubscriptionStateFilter.Active,
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

            var result = new List<SubscriptionDto>();
            foreach (var subResponse in subscriptions)
            {
                var sub = subResponse.Subscription;
                if (sub?.CustomerId == customerId)
                {
                    result.Add(MapSubscriptionDto(sub));
                }
            }

            _logger.LogInformation("Retrieved {SubscriptionCount} subscriptions for customer {CustomerId}", result.Count, customerId);
            return result;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing subscriptions: HTTP {StatusCode}", ex.Error.StatusCode);
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize subscriptions response");
            throw new InvalidOperationException("Provider returned an unparseable response", ex);
        }
    }

    private static SubscriptionDto MapSubscriptionDto(Subscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            State = sub.State?.ToString() ?? "Unknown",
            ProductHandle = sub.ProductHandle,
            CreatedAt = sub.CreatedAt,
            NextBillingAt = sub.NextBillingAt,
            CurrentPeriodStartsAt = sub.CurrentPeriodStartsAt,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            BalanceInCents = sub.BalanceInCents ?? 0
        };
    }
}

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "Month";
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public long BalanceInCents { get; set; }
}
