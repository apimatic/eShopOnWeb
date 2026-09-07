using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using CreateSubscriptionRequestDto = Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.CreateSubscriptionRequest;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioSubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IUserMaxioCustomerMappingCache _customerCache;
    private readonly MaxioOptions _maxioOptions;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IUserMaxioCustomerMappingCache customerCache,
        IOptions<MaxioOptions> maxioOptions,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _customerCache = customerCache;
        _maxioOptions = maxioOptions.Value;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlanDto>();
        var planHandles = new[] { "eshop-pro", "basic-plan" };

        foreach (var handle in planHandles)
        {
            try
            {
                var response = await _client.Products.ReadProductByHandle(handle, ct: ct);
                var product = response.Product;

                if (product != null)
                {
                    plans.Add(new SubscriptionPlanDto(
                        Handle: product.Handle ?? handle,
                        Name: product.Name ?? handle,
                        PriceInCents: product.PriceInCents ?? 0,
                        IntervalInMonths: product.Interval ?? 1,
                        Description: product.Description
                    ));
                }
            }
            catch (SdkException<RawError> ex)
            {
                _logger.LogWarning($"Failed to fetch plan {handle}: {(int)ex.Error.StatusCode}");
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Failed to parse product response for {handle}: {ex.Message}");
                throw new InvalidOperationException($"Failed to read product plan {handle}", ex);
            }
        }

        return plans;
    }

    public async Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
        string userId,
        string planHandle,
        CancellationToken ct)
    {
        var customerId = await GetOrCreateMaxioCustomerAsync(userId, ct);

        try
        {
            var subscriptionData = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = userId
            };

            var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = subscriptionData
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: request,
                ct: ct);

            var subscription = response.Subscription;
            if (subscription?.Id == null)
                throw new InvalidOperationException("Subscription creation returned no ID");

            return new CreateSubscriptionResponse(
                SubscriptionId: (int)subscription.Id,
                State: subscription.State?.ToString() ?? "unknown",
                NextBillingAt: subscription.NextAssessmentAt
            );
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationError))
            {
                _logger.LogWarning($"Subscription validation error: {validationError}");
                throw new InvalidOperationException("Invalid subscription parameters", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError($"Subscription creation failed: {(int)raw.StatusCode}");
                throw new InvalidOperationException("Failed to create subscription", ex);
            }
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Subscription creation failed: {(int)ex.Error.StatusCode}");
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to parse subscription response: {ex.Message}");
            throw new InvalidOperationException("Failed to process subscription response", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(
        string userId,
        CancellationToken ct)
    {
        if (!_customerCache.TryGet(userId, out var customerId))
        {
            return new List<SubscriptionDto>();
        }

        var subscriptions = new List<SubscriptionDto>();

        try
        {
            var results = await _client.Subscriptions.ListSubscriptions(
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

            foreach (var subscriptionResponse in results)
            {
                var subscription = subscriptionResponse.Subscription;
                if (subscription?.Id != null && subscription.Customer?.Id == customerId)
                {
                    subscriptions.Add(new SubscriptionDto(
                        SubscriptionId: (int)subscription.Id,
                        ProductHandle: subscription.Product?.Handle ?? "unknown",
                        ProductName: subscription.Product?.Name ?? "Unknown Plan",
                        NextBillingAt: subscription.NextAssessmentAt,
                        State: subscription.State?.ToString() ?? "unknown"
                    ));
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning($"Failed to list subscriptions for customer {customerId}: {(int)ex.Error.StatusCode}");
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to parse subscriptions response: {ex.Message}");
        }

        return subscriptions;
    }

    private async Task<int> GetOrCreateMaxioCustomerAsync(string userId, CancellationToken ct)
    {
        if (_customerCache.TryGet(userId, out var customerId))
        {
            return customerId;
        }

        try
        {
            var customer = await ReadCustomerByReferenceAsync(userId, ct);
            if (customer?.Id != null)
            {
                _customerCache.Set(userId, (int)customer.Id);
                return (int)customer.Id;
            }
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            // Customer doesn't exist, create a new one
        }
        catch (JsonException ex)
        {
            _logger.LogWarning($"Failed to parse customer lookup response: {ex.Message}");
        }

        return await CreateMaxioCustomerAsync(userId, ct);
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if ((int)ex.Error.StatusCode == 404)
                return null;
            throw;
        }
    }

    private async Task<int> CreateMaxioCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var customerData = new CreateCustomer
            {
                FirstName = "Customer",
                LastName = userId,
                Email = $"{userId}@eshop.local",
                Reference = userId
            };

            var request = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = customerData
            };

            var response = await _client.Customers.CreateCustomer(
                body: request,
                ct: ct);

            var customer = response.Customer;
            if (customer?.Id == null)
                throw new InvalidOperationException("Customer creation returned no ID");

            _customerCache.Set(userId, (int)customer.Id);
            return (int)customer.Id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validationError))
            {
                _logger.LogWarning($"Customer validation error: {validationError}");
                throw new InvalidOperationException("Invalid customer data", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError($"Customer creation failed: {(int)raw.StatusCode}");
                throw new InvalidOperationException("Failed to create customer", ex);
            }
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Customer creation failed: {(int)ex.Error.StatusCode}");
            throw new InvalidOperationException("Failed to create customer", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to parse customer response: {ex.Message}");
            throw new InvalidOperationException("Failed to process customer response", ex);
        }
    }
}
