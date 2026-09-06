using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;
using CreateSubscriptionRequest = MaxioAdvancedBilling.Models.CreateSubscriptionRequest;
using CustomerAttributes = MaxioAdvancedBilling.Models.CustomerAttributes;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string planHandle, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingInterval { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ConcurrentDictionary<string, int> _userSubscriptionMap;

    public MaxioSubscriptionService(IOptions<MaxioSettings> options)
    {
        var settings = options.Value;

        var authOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            },
            Environment = ServerEnvironment.Us
        };

        var httpClient = new HttpClient();
        _client = new MaxioAdvancedBillingClient(httpClient, authOptions);
        _productFamilyHandle = settings.ProductFamilyHandle;
        _userSubscriptionMap = new ConcurrentDictionary<string, int>();
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct)
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
                perPage: 20,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();
            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                if (product?.Handle != null && (product.Handle.StartsWith("eshop-") || product.Handle == "basic-plan"))
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Name = product.Name ?? string.Empty,
                        Handle = product.Handle,
                        Price = (product.PriceInCents ?? 0) / 100m,
                        BillingInterval = product.IntervalUnit?.ToString() ?? "month"
                    });
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to fetch subscription plans: {ex.Error.StatusCode}", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string planHandle, CancellationToken ct)
    {
        int customerId = await GetOrCreateCustomerAsync(userId, ct);

        try
        {
            var createSub = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId
            };

            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = createSub
            };

            var subscriptionResponse = await _client.Subscriptions.CreateSubscription(subscriptionRequest, ct: ct);
            var subscription = subscriptionResponse?.Subscription;

            if (subscription != null && subscription.Id.HasValue)
            {
                _userSubscriptionMap[userId] = subscription.Id.Value;
            }

            var state = "unknown";
            if (subscription?.State != null)
            {
                try { state = subscription.State.Value.ToString(); } catch { }
            }

            return new SubscriptionDto
            {
                Id = subscription?.Id ?? 0,
                CustomerId = 0,
                ProductHandle = planHandle,
                State = state,
                CurrentPeriodEndsAt = subscription?.CurrentPeriodEndsAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                var errorMessage = errorResponse?.Errors != null
                    ? string.Join(", ", errorResponse.Errors)
                    : "Unknown subscription creation error";
                throw new InvalidOperationException($"Failed to create subscription: {errorMessage}", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw new InvalidOperationException($"Failed to create subscription: {raw.StatusCode}", ex);
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct)
    {
        int customerId = await GetOrCreateCustomerAsync(userId, ct);

        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);

            var result = new List<SubscriptionDto>();
            foreach (var subscriptionResponse in subscriptions)
            {
                var subscription = subscriptionResponse.Subscription;
                var state = "unknown";
                if (subscription?.State != null)
                {
                    try { state = subscription.State.Value.ToString(); } catch { }
                }

                result.Add(new SubscriptionDto
                {
                    Id = subscription?.Id ?? 0,
                    CustomerId = 0,
                    ProductHandle = string.Empty,
                    State = state,
                    CurrentPeriodEndsAt = subscription?.CurrentPeriodEndsAt
                });
            }

            return result;
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to fetch user subscriptions: {ex.Error.StatusCode}", ex);
        }
    }

    private async Task<int> GetOrCreateCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var customerResponse = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
            return customerResponse.Customer?.Id ?? throw new InvalidOperationException("Customer ID not found");
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return await CreateNewCustomerAsync(userId, ct);
            }
            throw new InvalidOperationException($"Failed to lookup customer: {ex.Error.StatusCode}", ex);
        }
    }

    private async Task<int> CreateNewCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var attrs = new MaxioAdvancedBilling.Models.CreateCustomer
            {
                Reference = userId,
                FirstName = "User",
                LastName = userId.Substring(0, Math.Min(20, userId.Length)),
                Email = $"{userId}@eshop.local"
            };

            var customerRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = attrs
            };

            var response = await _client.Customers.CreateCustomer(customerRequest, ct: ct);
            return response?.Customer?.Id ?? throw new InvalidOperationException("Customer ID not found in response");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                throw new InvalidOperationException("Failed to create customer: validation error", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw new InvalidOperationException($"Failed to create customer: {raw.StatusCode}", ex);
            }
            throw;
        }
    }
}
