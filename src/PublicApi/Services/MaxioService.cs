using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioService : IMaxioService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _configuration;
    private readonly IMaxioCustomerService _customerService;

    public MaxioService(MaxioAdvancedBillingClient client, IConfiguration configuration, IMaxioCustomerService customerService)
    {
        _client = client;
        _configuration = configuration;
        _customerService = customerService;
    }

    public async Task<List<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken ct = default)
    {
        var plans = new List<SubscriptionPlanDto>();

        try
        {
            var productFamilyHandle = _configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: productFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            if (response != null)
            {
                foreach (var product in response)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Product?.Id ?? 0,
                        Name = product.Product?.Name,
                        Handle = product.Product?.Handle,
                        PriceInCents = product.Product?.PriceInCents ?? 0,
                        Interval = product.Product?.Interval?.ToString(),
                        IntervalUnit = product.Product?.IntervalUnit?.ToString(),
                        Description = product.Product?.Description
                    });
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to list products: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to process subscription plans response.", ex);
        }

        return plans;
    }

    public async Task<(int?, string? Reference)> GetOrCreateMaxioCustomerAsync(string userEmail, string userId, CancellationToken ct = default)
    {
        // Try to find existing customer by reference (userId)
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: userId, ct: ct);
            if (response?.Customer != null && response.Customer.Id.HasValue)
            {
                await _customerService.StoreMaxioCustomerMappingAsync(userId, response.Customer.Id.Value);
                return (response.Customer.Id, response.Customer.Reference);
            }
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"Failed to read customer: HTTP {(int)ex.Error.StatusCode}", ex);
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to process customer response.", ex);
        }

        // Customer doesn't exist, create one
        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = userEmail.Split('@')[0] ?? "User",
                    LastName = "Account",
                    Email = userEmail,
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(body: createRequest, ct: ct);
            if (response?.Customer != null && response.Customer.Id.HasValue)
            {
                await _customerService.StoreMaxioCustomerMappingAsync(userId, response.Customer.Id.Value);
                return (response.Customer.Id, response.Customer.Reference);
            }

            throw new InvalidOperationException("Failed to create customer: no customer returned.");
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to create customer: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to process customer creation response.", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    PaymentCollectionMethod = CollectionMethod.Automatic
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: createRequest, ct: ct);
            if (response?.Subscription != null)
            {
                var subscription = response.Subscription;
                return new SubscriptionDto
                {
                    Id = subscription.Id,
                    State = subscription.State?.Value,
                    ProductHandle = subscription.Product?.Handle,
                    ProductName = subscription.Product?.Name,
                    ProductPriceInCents = subscription.ProductPriceInCents,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    ActivatedAt = subscription.ActivatedAt,
                    CanceledAt = subscription.CanceledAt,
                    Reference = subscription.Reference
                };
            }

            throw new InvalidOperationException("Failed to create subscription: no subscription returned.");
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to create subscription: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to process subscription creation response.", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        var subscriptions = new List<SubscriptionDto>();

        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
            if (response != null)
            {
                foreach (var sub in response)
                {
                    subscriptions.Add(new SubscriptionDto
                    {
                        Id = sub.Subscription?.Id ?? 0,
                        State = sub.Subscription?.State?.Value,
                        ProductHandle = sub.Subscription?.Product?.Handle,
                        ProductName = sub.Subscription?.Product?.Name,
                        ProductPriceInCents = sub.Subscription?.ProductPriceInCents,
                        CurrentPeriodEndsAt = sub.Subscription?.CurrentPeriodEndsAt,
                        NextAssessmentAt = sub.Subscription?.NextAssessmentAt,
                        ActivatedAt = sub.Subscription?.ActivatedAt,
                        CanceledAt = sub.Subscription?.CanceledAt,
                        Reference = sub.Subscription?.Reference
                    });
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to list subscriptions: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to process subscriptions response.", ex);
        }

        return subscriptions;
    }
}
