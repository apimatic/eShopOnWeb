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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _configuration;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IConfiguration configuration, IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MaxioSubscriptionPlan[]> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var plans = new List<MaxioSubscriptionPlan>();
            var productHandles = new[] { "eshop-pro", "eshop-basic" };

            foreach (var handle in productHandles)
            {
                try
                {
                    var response = await _client.Products.ReadProductByHandle(apiHandle: handle, ct: ct);
                    if (response?.Product != null)
                    {
                        decimal? price = null;
                        if (response.Product.PriceInCents.HasValue)
                        {
                            price = response.Product.PriceInCents.Value / 100m;
                        }

                        plans.Add(new MaxioSubscriptionPlan(
                            Id: response.Product.Id ?? 0,
                            Handle: response.Product.Handle ?? handle,
                            Name: response.Product.Name ?? handle,
                            Description: response.Product.Description,
                            Price: price));
                    }
                }
                catch (SdkException<RawError> ex)
                {
                    _logger.LogWarning($"Failed to fetch plan {handle}: HTTP {(int)ex.Error.StatusCode}");
                }
            }

            return plans.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error fetching subscription plans from Maxio: {ex.Message}");
            throw;
        }
    }

    public async Task<MaxioSubscription?> CreateSubscriptionAsync(string userReference, string productHandle, CancellationToken ct = default)
    {
        try
        {
            await EnsureCustomerExistsAsync(userReference, ct);

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = userReference,
                    Reference = $"sub-{userReference}-{productHandle}-{DateTime.UtcNow.Ticks}"
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: body, ct: ct);

            if (response?.Subscription != null)
            {
                var nextBilling = response.Subscription.CurrentPeriodEndsAt
                    ?? response.Subscription.NextAssessmentAt;

                DateTime? nextBillingDateTime = nextBilling?.UtcDateTime;

                return new MaxioSubscription(
                    Id: response.Subscription.Id ?? 0,
                    State: response.Subscription.State?.ToString() ?? "unknown",
                    NextBillingAt: nextBillingDateTime,
                    Balance: response.Subscription.BalanceInCents.HasValue
                        ? response.Subscription.BalanceInCents.Value / 100m
                        : null,
                    ProductHandle: response.Subscription.Product?.Handle);
            }

            return null;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                _logger.LogWarning($"Subscription creation validation error for user {userReference}: {string.Join(", ", errorList.Errors ?? new List<string>())}");
                throw new InvalidOperationException("Subscription creation failed: validation error", ex);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogWarning($"Subscription creation HTTP {(int)raw.StatusCode} for user {userReference}");
                throw new InvalidOperationException($"Subscription creation failed: {(int)raw.StatusCode}", ex);
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error creating subscription for user {userReference}: {ex.Message}");
            throw;
        }
    }

    public async Task<MaxioSubscription[]> GetUserSubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        try
        {
            var customerId = await GetCustomerIdAsync(userReference, ct);
            if (customerId == null)
            {
                return Array.Empty<MaxioSubscription>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId.Value, ct: ct);

            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s =>
                {
                    var nextBilling = s.Subscription.CurrentPeriodEndsAt
                        ?? s.Subscription.NextAssessmentAt;

                    DateTime? nextBillingDateTime = nextBilling?.UtcDateTime;

                    return new MaxioSubscription(
                        Id: s.Subscription.Id ?? 0,
                        State: s.Subscription.State?.ToString() ?? "unknown",
                        NextBillingAt: nextBillingDateTime,
                        Balance: s.Subscription.BalanceInCents.HasValue
                            ? s.Subscription.BalanceInCents.Value / 100m
                            : (decimal?)null,
                        ProductHandle: s.Subscription.Product?.Handle);
                })
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning($"Failed to list subscriptions for user {userReference}: HTTP {(int)ex.Error.StatusCode}");
            throw new InvalidOperationException("Failed to retrieve subscriptions", ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error retrieving subscriptions for user {userReference}: {ex.Message}");
            throw;
        }
    }

    private async Task EnsureCustomerExistsAsync(string userReference, CancellationToken ct)
    {
        try
        {
            await _client.Customers.ReadCustomerByReference(reference: userReference, ct: ct);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            await CreateCustomerAsync(userReference, ct);
        }
    }

    private async Task CreateCustomerAsync(string userReference, CancellationToken ct)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                Reference = userReference,
                FirstName = "eShop",
                LastName = "Customer",
                Email = $"{userReference}@eshop.local"
            }
        };

        try
        {
            await _client.Customers.CreateCustomer(body: body, ct: ct);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var customerError))
            {
                _logger.LogWarning($"Customer creation validation error for {userReference}");
                throw new InvalidOperationException("Customer creation failed", ex);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                if ((int)raw.StatusCode == 422)
                {
                    _logger.LogWarning($"Customer already exists for reference {userReference}");
                    return;
                }
                _logger.LogWarning($"Customer creation HTTP {(int)raw.StatusCode} for {userReference}");
                throw new InvalidOperationException($"Customer creation failed: {(int)raw.StatusCode}", ex);
            }
            throw;
        }
    }

    private async Task<int?> GetCustomerIdAsync(string userReference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: userReference, ct: ct);
            return response?.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
    }
}
