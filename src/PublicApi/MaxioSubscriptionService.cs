using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userReference, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> GetUserSubscriptionsAsync(string userReference, CancellationToken ct = default);
    Task<int?> GetOrCreateCustomerIdAsync(string userReference, string email, CancellationToken ct = default);
}

public class SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public object? Subscription { get; set; }
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IConfiguration configuration,
        HttpClient httpClient,
        ILogger<MaxioSubscriptionService> logger)
    {
        _logger = logger;

        var apiKey = configuration["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey not configured");
        var subdomain = configuration["Maxio:Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain not configured");
        var environment = configuration["Maxio:Environment"] ?? "US";
        _productFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? throw new InvalidOperationException("Maxio:ProductFamilyHandle not configured");
        var baseUrl = configuration["Maxio:BaseUrl"];

        var serverEnvironment = environment.Equals("EU", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Eu
            : ServerEnvironment.Us;

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = serverEnvironment,
            BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }
        };

        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                _productFamilyHandle,
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

            return response.Select(pr => new SubscriptionPlanDto
            {
                Id = pr.Product?.Id,
                Handle = pr.Product?.Handle,
                Name = pr.Product?.Name,
                Description = pr.Product?.Description,
                PriceInCents = pr.Product?.PriceInCents,
                Interval = pr.Product?.Interval,
                IntervalUnit = pr.Product?.IntervalUnit?.Value
            }).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio");
            throw new InvalidOperationException("Failed to fetch subscription plans", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userReference,
        string productHandle,
        CancellationToken ct = default)
    {
        var customerId = await GetOrCreateCustomerIdAsync(userReference, userReference, ct);
        if (customerId is null)
            throw new InvalidOperationException("Failed to create or retrieve customer");

        try
        {
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = CollectionMethod.Invoice
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body, ct: ct);

            return new SubscriptionDto
            {
                Subscription = response.Subscription
            };
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error creating subscription for product {ProductHandle}", productHandle);
            throw new InvalidOperationException($"Failed to create subscription for product {productHandle}", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetUserSubscriptionsAsync(
        string userReference,
        CancellationToken ct = default)
    {
        try
        {
            var customerId = await GetOrCreateCustomerIdAsync(userReference, userReference, ct);
            if (customerId is null)
                return [];

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

            return response
                .Where(sr => true)
                .Select(sr => new SubscriptionDto
                {
                    Subscription = sr.Subscription
                }).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user {UserReference}", userReference);
            throw new InvalidOperationException("Failed to fetch subscriptions", ex);
        }
    }

    public async Task<int?> GetOrCreateCustomerIdAsync(
        string userReference,
        string email,
        CancellationToken ct = default)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(userReference, ct: ct);
            return existing.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when ((int?)ex.Error.StatusCode == 404)
        {
            _logger.LogInformation("Customer not found for reference {UserReference}, creating new customer", userReference);

            try
            {
                var body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        Reference = userReference,
                        Email = email,
                        FirstName = "User",
                        LastName = userReference
                    }
                };

                var response = await _client.Customers.CreateCustomer(body, ct: ct);
                return response.Customer?.Id;
            }
            catch (SdkException<RawError> createEx)
            {
                _logger.LogError(createEx, "Error creating customer for reference {UserReference}", userReference);
                throw new InvalidOperationException($"Failed to create customer", createEx);
            }
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error checking customer for reference {UserReference}", userReference);
            throw new InvalidOperationException("Failed to check customer", ex);
        }
    }
}
