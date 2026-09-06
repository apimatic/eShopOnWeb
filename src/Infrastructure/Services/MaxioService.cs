using System;
using System.Collections.Generic;
using System.Net.Http;
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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioService : IMaxioService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(IConfiguration configuration, ILogger<MaxioService> logger)
    {
        _logger = logger;
        _settings = new MaxioSettings
        {
            ApiKey = configuration["Maxio:ApiKey"],
            Subdomain = configuration["Maxio:Subdomain"],
            Environment = configuration["Maxio:Environment"] ?? "us",
            ProductFamilyHandle = configuration["Maxio:ProductFamilyHandle"],
            BaseUrl = configuration["Maxio:BaseUrl"]
        };

        ValidateSettings();

        var httpClient = new HttpClient();
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = _settings.ApiKey!,
                Password = "x"
            }
        };

        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = _settings.BaseUrl;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrEmpty(_settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey configuration is required");
        if (string.IsNullOrEmpty(_settings.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain configuration is required");
        if (string.IsNullOrEmpty(_settings.ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle configuration is required");
    }

    public async Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct = default)
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
                perPage: 100,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();

            foreach (var productResponse in response)
            {
                var product = productResponse.Product;
                if (product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Handle = product.Handle ?? string.Empty,
                        Name = product.Name ?? string.Empty,
                        Description = product.Description ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 0,
                        IntervalUnit = product.IntervalUnit?.Value ?? "month"
                    });
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to list products: {StatusCode} {Message}",
                (int)ex.Error.StatusCode, ex.Error.ReadAsString());
            throw new InvalidOperationException("Failed to retrieve subscription plans from Maxio", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing products");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var customer = await GetOrCreateCustomerAsync(userId, ct);

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Customer?.Id,
                    ProductHandle = productHandle,
                    PaymentCollectionMethod = CollectionMethod.Automatic
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: body, ct: ct);

            if (response?.Subscription == null)
                throw new InvalidOperationException("No subscription returned from Maxio");

            var subscription = response.Subscription;

            return new SubscriptionDto
            {
                Id = subscription.Id ?? 0,
                CustomerId = customer.Customer?.Id ?? 0,
                State = subscription.State?.Value ?? string.Empty,
                ProductHandle = productHandle,
                ProductPriceInCents = subscription.ProductPriceInCents ?? 0,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt,
                UpdatedAt = subscription.UpdatedAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var message = string.Join("; ", errorList.Errors ?? new List<string>());
                _logger.LogError("Subscription creation failed: {Message}", message);
                throw new InvalidOperationException($"Failed to create subscription: {message}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError("Subscription creation failed: {StatusCode} {Message}",
                    (int)raw.StatusCode, raw.ReadAsString());
            }

            throw new InvalidOperationException("Failed to create subscription", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Maxio subscription response");
            throw new InvalidOperationException("Failed to process subscription response from Maxio", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var customer = await GetOrCreateCustomerAsync(userId, ct);

            var response = await _client.Subscriptions.ListSubscriptions(
                state: null,
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

            var subscriptions = new List<SubscriptionDto>();

            foreach (var subResponse in response)
            {
                var sub = subResponse.Subscription;
                if (sub != null && sub.Customer?.Id == customer.Customer?.Id)
                {
                    subscriptions.Add(new SubscriptionDto
                    {
                        Id = sub.Id ?? 0,
                        CustomerId = sub.Customer?.Id ?? 0,
                        State = sub.State?.Value ?? string.Empty,
                        ProductHandle = sub.Product?.Handle ?? string.Empty,
                        ProductPriceInCents = sub.ProductPriceInCents ?? 0,
                        CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                        NextAssessmentAt = sub.NextAssessmentAt,
                        CreatedAt = sub.CreatedAt,
                        UpdatedAt = sub.UpdatedAt
                    });
                }
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to list subscriptions: {StatusCode} {Message}",
                (int)ex.Error.StatusCode, ex.Error.ReadAsString());
            throw new InvalidOperationException("Failed to retrieve subscriptions from Maxio", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private async Task<CustomerResponse> GetOrCreateCustomerAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: userId, ct: ct);
            return response;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return await CreateCustomerAsync(userId, ct);
        }
    }

    private async Task<CustomerResponse> CreateCustomerAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "eShop",
                    LastName = "Customer",
                    Email = $"{userId}@eshop.local",
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(body: body, ct: ct);

            if (response?.Customer == null)
                throw new InvalidOperationException("No customer returned from Maxio");

            return response;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var error))
            {
                var message = error.Errors?.ToString() ?? "Unknown error";
                _logger.LogError("Customer creation failed: {Message}", message);
                throw new InvalidOperationException($"Failed to create Maxio customer: {message}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError("Customer creation failed: {StatusCode} {Message}",
                    (int)raw.StatusCode, raw.ReadAsString());
            }

            throw new InvalidOperationException("Failed to create Maxio customer", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Maxio customer response");
            throw new InvalidOperationException("Failed to process customer response from Maxio", ex);
        }
    }
}
