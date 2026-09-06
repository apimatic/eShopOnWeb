using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Errors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IConfiguration configuration,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        var plans = new List<SubscriptionPlanDto>();
        var planHandles = new[] { "eshop-pro", "basic-plan" };

        foreach (var handle in planHandles)
        {
            try
            {
                var product = await _client.Products.ReadProductByHandle(apiHandle: handle, ct: ct);
                var productData = product?.Product;

                if (productData != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = productData.Id ?? 0,
                        Handle = productData.Handle ?? handle,
                        Name = productData.Name ?? string.Empty,
                        Description = productData.Description,
                        Price = (productData.PriceInCents ?? 0) / 100m,
                        IntervalDays = (productData.Interval ?? 1) * 30
                    });
                }
            }
            catch (SdkException<RawError> ex)
            {
                _logger.LogWarning($"Failed to fetch plan {handle}: HTTP {ex.Error.StatusCode}");
            }
        }

        return plans;
    }

    public async Task<(int CustomerId, bool IsNewCustomer)> EnsureMaxioCustomerAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        CancellationToken ct = default)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(reference: userId, ct: ct);
            if (existing?.Customer?.Id.HasValue == true)
            {
                return (existing.Customer.Id.Value, false);
            }
        }
        catch (SdkException<RawError> ex)
        {
            if ((int)ex.Error.StatusCode == 404)
            {
                _logger.LogInformation($"Customer with reference {userId} not found, will create new one");
            }
            else
            {
                _logger.LogError($"Error looking up customer: HTTP {ex.Error.StatusCode}");
                throw;
            }
        }

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

        try
        {
            var created = await _client.Customers.CreateCustomer(body: createRequest, ct: ct);
            if (created?.Customer?.Id.HasValue == true)
            {
                _logger.LogInformation($"Created new Maxio customer {created.Customer.Id} for user {userId}");
                return (created.Customer.Id.Value, true);
            }

            throw new InvalidOperationException("Customer creation failed: no ID returned");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResp))
            {
                var errorMsg = "Failed to create customer";
                if (errorResp?.Errors != null)
                {
                    errorMsg = $"Failed to create customer: {errorResp.Errors}";
                }
                throw new InvalidOperationException(errorMsg, ex);
            }

            throw new InvalidOperationException("Failed to create customer", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken ct = default)
    {
        var createSubscription = new CreateSubscription
        {
            CustomerId = customerId,
            ProductHandle = productHandle,
            PaymentCollectionMethod = CollectionMethod.Automatic
        };

        var createSubRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = createSubscription
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body: createSubRequest, ct: ct);
            var subscription = response?.Subscription;

            if (subscription?.Id == null)
            {
                throw new InvalidOperationException("Subscription creation failed: no ID returned");
            }

            return MapSubscriptionDto(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResp))
            {
                var errorMsg = string.Join("; ", errorResp.Errors ?? new List<string>());
                throw new InvalidOperationException($"Failed to create subscription: {errorMsg}", ex);
            }

            throw new InvalidOperationException("Failed to create subscription", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var subscriptionResponses = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
            if (subscriptionResponses == null)
                return new List<SubscriptionDto>();

            var subscriptions = new List<SubscriptionDto>();
            foreach (var response in subscriptionResponses)
            {
                if (response?.Subscription != null)
                {
                    subscriptions.Add(MapSubscriptionDto(response.Subscription));
                }
            }
            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to fetch customer subscriptions: HTTP {ex.Error.StatusCode}");
            throw;
        }
    }

    private SubscriptionDto MapSubscriptionDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.ToString() ?? "unknown",
            BalanceInCents = subscription.BalanceInCents ?? 0,
            ProductPriceInCents = subscription.ProductPriceInCents ?? 0,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt?.DateTime,
            NextAssessmentAt = subscription.NextAssessmentAt?.DateTime,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            CreatedAt = subscription.CreatedAt?.DateTime
        };
    }
}
