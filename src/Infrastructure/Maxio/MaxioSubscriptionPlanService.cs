using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionPlanService : ISubscriptionPlanService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionPlanService> _logger;

    public MaxioSubscriptionPlanService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionPlanService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default)
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

            return products
                .Where(p => p.Product?.ArchivedAt == null)
                .Select(p => new SubscriptionPlanDto(
                    Id: p.Product?.Id ?? 0,
                    Name: p.Product?.Name ?? string.Empty,
                    Handle: p.Product?.Handle ?? string.Empty,
                    Description: p.Product?.Description ?? string.Empty,
                    Price: (p.Product?.PriceInCents ?? 0) / 100m,
                    IntervalUnit: p.Product?.IntervalUnit?.Value ?? "month",
                    Interval: p.Product?.Interval ?? 1))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list products from Maxio: HTTP {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to retrieve subscription plans: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error listing Maxio products");
            throw new InvalidOperationException("Failed to retrieve subscription plans.", ex);
        }
    }

    public async Task<SubscriptionResultDto> SubscribeUserAsync(string userId, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var customerId = await EnsureCustomerExistsAsync(userId, ct);
            var subscriptionReference = $"{userId}-{productHandle}";

            var subscriptionResponse = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        CustomerId = customerId,
                        ProductHandle = productHandle,
                        Reference = subscriptionReference
                    }
                },
                ct);

            var subscription = subscriptionResponse.Subscription
                ?? throw new InvalidOperationException("Subscription creation returned no subscription data.");

            return new SubscriptionResultDto(
                SubscriptionId: subscription.Id ?? 0,
                State: subscription.State?.Value ?? "unknown",
                CreatedAt: subscription.CreatedAt ?? DateTimeOffset.UtcNow,
                NextAssessmentAt: subscription.NextAssessmentAt,
                ProductName: subscription.Product?.Name ?? string.Empty);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                _logger.LogWarning("Subscription creation rejected: {Errors}", errorList);
                throw new InvalidOperationException($"Subscription creation rejected: duplicate or invalid request.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError(ex, "Subscription creation failed: HTTP {StatusCode} {Body}", raw.StatusCode, raw.ReadAsString());
                throw new InvalidOperationException($"Subscription creation failed: HTTP {(int)raw.StatusCode}", ex);
            }
            throw new InvalidOperationException("Subscription creation failed.", ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var custError))
            {
                _logger.LogWarning("Customer creation rejected during subscribe: {Errors}", custError);
                throw new InvalidOperationException($"Customer setup failed during subscription: duplicate or invalid request.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError(ex, "Customer creation failed during subscribe: HTTP {StatusCode}", raw.StatusCode);
                throw new InvalidOperationException($"Customer setup failed: HTTP {(int)raw.StatusCode}", ex);
            }
            throw new InvalidOperationException("Customer setup failed during subscription.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio API error during subscribe: HTTP {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException($"Maxio API error: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error during subscribe");
            throw new InvalidOperationException("Subscription creation failed.", ex);
        }
    }

    public async Task<IReadOnlyList<UserSubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            CustomerResponse customerResponse;
            try
            {
                customerResponse = await _client.Customers.ReadCustomerByReference(userId, ct);
            }
            catch (SdkException<RawError>)
            {
                return Array.Empty<UserSubscriptionDto>();
            }

            var maxioCustomerId = customerResponse.Customer?.Id
                ?? throw new InvalidOperationException("Customer found but has no ID.");

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(maxioCustomerId, ct);

            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => new UserSubscriptionDto(
                    SubscriptionId: s.Subscription!.Id ?? 0,
                    State: s.Subscription.State?.Value ?? "unknown",
                    ProductName: s.Subscription.Product?.Name ?? string.Empty,
                    Price: 0,
                    CreatedAt: s.Subscription.CreatedAt ?? DateTimeOffset.UtcNow,
                    NextAssessmentAt: s.Subscription.NextAssessmentAt,
                    CanceledAt: s.Subscription.CanceledAt))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions: HTTP {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to retrieve subscriptions: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions");
            throw new InvalidOperationException("Failed to retrieve subscriptions.", ex);
        }
    }

    private async Task<int> EnsureCustomerExistsAsync(string userId, CancellationToken ct)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(userId, ct);
            return existing.Customer?.Id
                ?? throw new InvalidOperationException("Customer found but has no ID.");
        }
        catch (SdkException<RawError>)
        {
            // Customer not found — create it
        }

        var createResponse = await _client.Customers.CreateCustomer(
            new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "eShop",
                    LastName = "Subscriber",
                    Email = $"{userId}@eshop.local",
                    Reference = userId
                }
            },
            ct);

        return createResponse.Customer?.Id
            ?? throw new InvalidOperationException("Customer creation returned no ID.");
    }
}
