using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioSubscriptionService
{
    Task<SubscriptionPlan[]> GetAvailablePlansAsync(CancellationToken ct = default);
    Task<(int CustomerId, string Reference)> EnsureCustomerAsync(string userReference, string email, string firstName, string lastName, CancellationToken ct = default);
    Task<SubscriptionInfo> CreateSubscriptionAsync(int customerId, string productHandle, string? reference = null, CancellationToken ct = default);
    Task<SubscriptionInfo[]> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
    Task<SubscriptionInfo> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionInfo
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IConfiguration config, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _logger = logger;

        var settings = new MaxioSettings();
        config.GetSection("Maxio").Bind(settings);
        _settings = settings;
    }

    public async Task<SubscriptionPlan[]> GetAvailablePlansAsync(CancellationToken ct = default)
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
                perPage: 20,
                ct: ct);

            return response
                .Select(p => p.Product)
                .Where(p => p != null)
                .Select(p => new SubscriptionPlan
                {
                    Id = p.Id ?? 0,
                    Handle = p.Handle ?? string.Empty,
                    Name = p.Name ?? string.Empty,
                    PriceInCents = p.PriceInCents ?? 0,
                    Interval = p.Interval ?? 1,
                    IntervalUnit = p.IntervalUnit?.ToString() ?? "month"
                })
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list products from Maxio");
            throw new InvalidOperationException("Failed to retrieve subscription plans", ex);
        }
    }

    public async Task<(int CustomerId, string Reference)> EnsureCustomerAsync(
        string userReference, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        try
        {
            var existingCustomer = await _client.Customers.ReadCustomerByReference(userReference, ct: ct);
            if (existingCustomer?.Customer?.Id.HasValue == true)
            {
                _logger.LogInformation("Customer already exists: {UserId}", userReference);
                return (existingCustomer.Customer.Id.Value, userReference);
            }
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            _logger.LogInformation("Customer not found, creating new one: {UserId}", userReference);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Unexpected error looking up customer");
            throw new InvalidOperationException("Failed to look up customer", ex);
        }

        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userReference
                }
            };

            var response = await _client.Customers.CreateCustomer(createRequest, ct: ct);
            if (response?.Customer?.Id.HasValue == true)
            {
                _logger.LogInformation("Created customer: {CustomerId} for user {UserId}", response.Customer.Id, userReference);
                return (response.Customer.Id.Value, userReference);
            }

            throw new InvalidOperationException("Customer creation returned empty response");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var error))
            {
                _logger.LogError("Customer creation failed with validation error: {@Error}", error);
                throw new InvalidOperationException($"Failed to create customer: validation error", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError("Customer creation failed: {Status} {Body}", raw.StatusCode, raw.ReadAsString());
                throw new InvalidOperationException($"Failed to create customer: {raw.StatusCode}", ex);
            }
            throw;
        }
    }

    public async Task<SubscriptionInfo> CreateSubscriptionAsync(
        int customerId, string productHandle, string? reference = null, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    Reference = reference
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(createRequest, ct: ct);
            if (response?.Subscription != null)
            {
                _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId}",
                    response.Subscription.Id, customerId);

                return MapSubscription(response.Subscription);
            }

            throw new InvalidOperationException("Subscription creation returned empty response");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var error))
            {
                _logger.LogError("Subscription creation failed with validation error: {@Error}", error);
                throw new InvalidOperationException("Failed to create subscription: validation error", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError("Subscription creation failed: {Status} {Body}", raw.StatusCode, raw.ReadAsString());
                throw new InvalidOperationException($"Failed to create subscription: {raw.StatusCode}", ex);
            }
            throw;
        }
    }

    public async Task<SubscriptionInfo[]> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);

            return response
                .Select(sr => sr.Subscription)
                .Where(s => s != null)
                .Select(MapSubscription)
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions for customer {CustomerId}", customerId);
            throw new InvalidOperationException("Failed to retrieve subscriptions", ex);
        }
    }

    public async Task<SubscriptionInfo> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: ct);

            if (response?.Subscription != null)
            {
                return MapSubscription(response.Subscription);
            }

            throw new InvalidOperationException("Subscription read returned empty response");
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to get subscription {SubscriptionId}", subscriptionId);
            throw new InvalidOperationException("Failed to retrieve subscription", ex);
        }
    }

    private static SubscriptionInfo MapSubscription(MaxioAdvancedBilling.Models.Subscription sub)
    {
        return new SubscriptionInfo
        {
            Id = sub.Id ?? 0,
            CustomerId = sub.Customer?.Id ?? 0,
            State = sub.State?.ToString() ?? "unknown",
            ProductPriceInCents = sub.ProductPriceInCents ?? 0,
            CreatedAt = sub.CreatedAt ?? DateTimeOffset.UtcNow,
            NextBillingAt = sub.NextAssessmentAt
        };
    }
}
