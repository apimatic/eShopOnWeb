using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioSubscriptionService
{
    Task<List<ProductDto>> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDetailsDto?> CreateSubscriptionAsync(string userId, string planHandle, CancellationToken ct = default);
    Task<List<SubscriptionDetailsDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        string apiKey,
        string subdomain,
        string productFamilyHandle,
        string? baseUrl,
        ILogger<MaxioSubscriptionService> logger)
    {
        _productFamilyHandle = productFamilyHandle;
        _logger = logger;

        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = apiKey,
                Password = "x"
            }
        };

        if (!string.IsNullOrEmpty(baseUrl))
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = subdomain;
        }

        options.Retry = options.Retry with
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxRetries = 3
        };

        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<List<ProductDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _productFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            return response
                .Select(p => new ProductDto
                {
                    Id = p.Product?.Id ?? 0,
                    Name = p.Product?.Name ?? string.Empty,
                    Handle = p.Product?.Handle ?? string.Empty,
                    PriceInCents = p.Product?.PriceInCents ?? 0,
                    Interval = p.Product?.Interval ?? 1,
                    IntervalUnit = p.Product?.IntervalUnit?.Value ?? "month"
                })
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            _logger.LogError(ex, "Failed to list products from Maxio");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing products from Maxio");
            throw;
        }
    }

    public async Task<SubscriptionDetailsDto?> CreateSubscriptionAsync(
        string userId,
        string planHandle,
        CancellationToken ct = default)
    {
        try
        {
            var customer = await EnsureCustomerExistsAsync(userId, ct);
            if (customer == null)
            {
                _logger.LogWarning("Failed to create or retrieve customer for userId: {UserId}", userId);
                return null;
            }

            var subscription = new CreateSubscription
            {
                CustomerReference = userId,
                ProductHandle = planHandle
            };

            var request = new CreateSubscriptionRequest
            {
                Subscription = subscription
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: request,
                ct: ct);

            if (response?.Subscription == null)
            {
                return null;
            }

            return new SubscriptionDetailsDto
            {
                Id = response.Subscription.Id ?? 0,
                State = response.Subscription.State?.Value ?? string.Empty,
                ProductPriceInCents = response.Subscription.ProductPriceInCents ?? 0,
                NextBillingDate = response.Subscription.NextAssessmentAt,
                CreatedAt = response.Subscription.CreatedAt,
                CurrentPeriodEndsAt = response.Subscription.CurrentPeriodEndsAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError(ex, "Failed to create subscription in Maxio for userId: {UserId}", userId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription in Maxio");
            throw;
        }
    }

    public async Task<List<SubscriptionDetailsDto>> GetUserSubscriptionsAsync(
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.ListSubscriptions(
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

            return response
                .Where(s => s.Subscription?.Customer?.Reference == userId)
                .Select(s => new SubscriptionDetailsDto
                {
                    Id = s.Subscription?.Id ?? 0,
                    State = s.Subscription?.State?.Value ?? string.Empty,
                    ProductPriceInCents = s.Subscription?.ProductPriceInCents ?? 0,
                    NextBillingDate = s.Subscription?.NextAssessmentAt,
                    CreatedAt = s.Subscription?.CreatedAt,
                    CurrentPeriodEndsAt = s.Subscription?.CurrentPeriodEndsAt
                })
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions from Maxio for userId: {UserId}", userId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions from Maxio");
            throw;
        }
    }

    private async Task<Customer?> EnsureCustomerExistsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);

            return existing?.Customer;
        }
        catch (SdkException<RawError>)
        {
            _logger.LogInformation("Customer not found for userId: {UserId}, creating new customer", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for existing customer");
            throw;
        }

        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "Customer",
                    LastName = userId,
                    Email = $"{userId}@eshop.local",
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: createRequest,
                ct: ct);

            return response?.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            _logger.LogError(ex, "Failed to create customer in Maxio for userId: {UserId}", userId);
            throw;
        }
    }
}

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public decimal Price => PriceInCents / 100m;
}

public class SubscriptionDetailsDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public decimal ProductPrice => ProductPriceInCents / 100m;
}
