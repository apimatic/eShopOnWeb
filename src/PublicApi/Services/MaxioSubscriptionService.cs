using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<SubscriptionPlanDto[]> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> SubscribeAsync(string userId, string userEmail, string firstName, string lastName, string planHandle, CancellationToken ct = default);
    Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId, string userEmail, CancellationToken ct = default);
}

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IConfiguration config, ILogger<MaxioSubscriptionService> logger, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _logger = logger;

        var apiKey = config["Maxio:ApiKey"];
        var subdomain = config["Maxio:Subdomain"];
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(subdomain))
        {
            throw new InvalidOperationException("Maxio API key and subdomain must be configured");
        }

        var httpClient = httpClientFactory.CreateClient();
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = apiKey,
                Password = "x"
            },
            Environment = ServerEnvironment.Us,
            Server = new ServerOptions
            {
                Production = new ProductionOptions
                {
                    Us = new ProductionOptions.UsOptions
                    {
                        Site = subdomain,
                        BaseUrl = config["Maxio:BaseUrl"] ?? "https://{site}.chargify.com"
                    }
                }
            }
        };

        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<SubscriptionPlanDto[]> GetSubscriptionPlansAsync(CancellationToken ct = default)
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

            return products
                .Select(p => p.Product)
                .Select(product => new SubscriptionPlanDto
                {
                    PlanId = product.Id.GetValueOrDefault(),
                    PlanName = product.Name,
                    PlanHandle = product.Handle,
                    PriceInCents = product.PriceInCents ?? 0,
                    BillingInterval = product.Interval ?? 1,
                    BillingPeriod = product.IntervalUnit?.Value ?? "month",
                    ExpiresNever = true
                })
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to list products: HTTP {(int)ex.Error.StatusCode}");
            throw;
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string userEmail, string firstName, string lastName, string planHandle, CancellationToken ct = default)
    {
        var customerReference = userId;
        var customer = await GetOrCreateCustomerAsync(customerReference, userEmail, firstName, lastName, ct);

        try
        {
            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = planHandle,
                    PaymentCollectionMethod = CollectionMethod.Automatic,
                    Reference = $"esshop-{userId}-{DateTime.UtcNow.Ticks}"
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(subscriptionRequest, ct: ct);
            var subscription = response.Subscription;

            return MapToSubscriptionDto(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationErrors))
            {
                _logger.LogError($"Subscription validation error: {validationErrors}");
                throw new InvalidOperationException("Subscription validation failed", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError($"Subscription creation failed: HTTP {(int)raw.StatusCode} - {raw.ReadAsString()}");
                throw new InvalidOperationException("Failed to create subscription", ex);
            }
            throw;
        }
    }

    public async Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId, string userEmail, CancellationToken ct = default)
    {
        var customerReference = userId;
        try
        {
            var customer = await _client.Customers.ReadCustomerByReference(customerReference, ct: ct);
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Customer.Id.GetValueOrDefault(), ct: ct);

            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => MapToSubscriptionDto(s.Subscription!))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            if ((int)ex.Error.StatusCode == 404)
            {
                _logger.LogInformation($"Customer not found for reference {customerReference}");
                return Array.Empty<SubscriptionDto>();
            }
            _logger.LogError($"Failed to retrieve subscriptions: HTTP {(int)ex.Error.StatusCode}");
            throw;
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return existing.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if ((int)ex.Error.StatusCode != 404)
            {
                throw;
            }
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
                    Reference = reference
                }
            };

            var response = await _client.Customers.CreateCustomer(createRequest, ct: ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var customerError))
            {
                _logger.LogError($"Customer creation error: {customerError}");
                throw new InvalidOperationException("Failed to create customer", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError($"Customer creation failed: HTTP {(int)raw.StatusCode} - {raw.ReadAsString()}");
                throw new InvalidOperationException("Failed to create customer", ex);
            }
            throw;
        }
    }

    private static SubscriptionDto MapToSubscriptionDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id.GetValueOrDefault(),
            State = subscription.State?.Value ?? "unknown",
            PriceInCents = subscription.ProductPriceInCents ?? 0,
            NextBillingDate = subscription.NextAssessmentAt,
            ActiveSince = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            ExpiresAt = subscription.ExpiresAt,
            CreatedAt = subscription.CreatedAt
        };
    }
}

public sealed class SubscriptionPlanDto
{
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int BillingInterval { get; set; }
    public string BillingPeriod { get; set; } = string.Empty;
    public bool ExpiresNever { get; set; }
}

public sealed class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActiveSince { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
