using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly Dictionary<string, int> _userCustomers = new();

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct)
    {
        try
        {
            var plans = new List<SubscriptionPlanDto>();
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

            foreach (var item in response)
            {
                if (item.Product?.Handle == null || item.Product.Id == null) continue;

                plans.Add(new SubscriptionPlanDto
                {
                    Id = item.Product.Id.Value,
                    Handle = item.Product.Handle,
                    Name = item.Product.Name ?? "Unknown Plan",
                    PriceInCents = item.Product.PriceInCents ?? 0,
                    Description = item.Product.Description
                });
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to fetch subscription plans: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to fetch subscription plans", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken ct)
    {
        try
        {
            // Ensure customer exists (idempotent)
            var customerId = await EnsureCustomerExistsAsync(userId, ct);

            // Create subscription
            var createSubRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    PaymentCollectionMethod = null
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(createSubRequest, ct: ct);

            if (response?.Subscription == null)
            {
                throw new InvalidOperationException("No subscription returned from Maxio");
            }

            var subscription = response.Subscription;
            return new SubscriptionDto
            {
                Id = subscription?.Id ?? 0,
                CustomerId = subscription?.Customer?.Id ?? 0,
                State = subscription?.State?.Value ?? "unknown",
                ProductPriceInCents = subscription?.ProductPriceInCents ?? 0,
                NextBillingDate = subscription?.NextAssessmentAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errors = string.Join(", ", errorList.Errors ?? new List<string>());
                throw new InvalidOperationException($"Failed to create subscription: {errors}", ex);
            }

            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw new InvalidOperationException($"Failed to create subscription: HTTP {(int)raw.StatusCode}", ex);
            }

            throw new InvalidOperationException("Failed to create subscription", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct)
    {
        try
        {
            var subscriptions = new List<SubscriptionDto>();

            // Get customer ID from cache or lookup
            if (!_userCustomers.TryGetValue(userId, out var customerId))
            {
                try
                {
                    var customer = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
                    if (customer?.Customer?.Id != null)
                    {
                        customerId = customer.Customer.Id.Value;
                        _userCustomers[userId] = customerId;
                    }
                    else
                    {
                        return subscriptions; // No customer found
                    }
                }
                catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
                {
                    return subscriptions; // No customer found
                }
            }

            // Get subscriptions for customer
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);

            foreach (var item in response)
            {
                var subscription = item.Subscription;
                if (subscription == null) continue;

                subscriptions.Add(new SubscriptionDto
                {
                    Id = subscription.Id ?? 0,
                    CustomerId = subscription.Customer?.Id ?? 0,
                    State = subscription.State?.Value ?? "unknown",
                    ProductPriceInCents = subscription.ProductPriceInCents ?? 0,
                    NextBillingDate = subscription.NextAssessmentAt
                });
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to fetch user subscriptions: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to fetch user subscriptions", ex);
        }
    }

    private async Task<int> EnsureCustomerExistsAsync(string userId, CancellationToken ct)
    {
        // Check if customer already cached
        if (_userCustomers.TryGetValue(userId, out var cachedId))
        {
            return cachedId;
        }

        try
        {
            // Try to read existing customer by reference
            var existing = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
            if (existing?.Customer?.Id != null)
            {
                _userCustomers[userId] = existing.Customer.Id.Value;
                return existing.Customer.Id.Value;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Customer doesn't exist, will create one
        }

        // Create new customer
        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = userId.Length > 0 ? userId[0].ToString() : "User",
                LastName = userId,
                Email = $"{userId}@eshop.local",
                Reference = userId
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(createRequest, ct: ct);
            if (response?.Customer?.Id != null)
            {
                _userCustomers[userId] = response.Customer.Id.Value;
                return response.Customer.Id.Value;
            }

            throw new InvalidOperationException("No customer ID returned from Maxio");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResp))
            {
                throw new InvalidOperationException("Failed to create customer: validation errors", ex);
            }

            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw new InvalidOperationException($"Failed to create customer: HTTP {(int)raw.StatusCode}", ex);
            }

            throw new InvalidOperationException("Failed to create customer", ex);
        }
    }
}
