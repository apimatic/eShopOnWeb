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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioCustomer = MaxioAdvancedBilling.Models.Customer;
using EShopSubscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IRepository<EShopSubscription> _subscriptionRepository;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IOptions<MaxioOptions> options,
        IRepository<EShopSubscription> subscriptionRepository,
        ILogger<MaxioSubscriptionService> logger)
    {
        _options = options.Value;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;

        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = _options.Subdomain == "cp-exp-1" ? ServerEnvironment.Us : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = _options.ApiKey,
                Password = "x"
            },
            Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(20)
            }
        };

        if (!string.IsNullOrEmpty(_options.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = _options.BaseUrl;
        }
        else
        {
            clientOptions.Server.Production.Us.Site = _options.Subdomain;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var plans = new List<SubscriptionPlanDto>();
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                _options.ProductFamilyHandle,
                null,  // dateField
                null,  // filter
                null,  // startDate
                null,  // endDate
                null,  // startDatetime
                null,  // endDatetime
                null,  // includeArchived
                null,  // include
                page: 1,
                perPage: 100,
                ct: ct);

            foreach (var product in products)
            {
                if (product.Product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Handle = product.Product.Handle ?? string.Empty,
                        Name = product.Product.Name ?? string.Empty,
                        PriceInCents = product.Product.PriceInCents ?? 0,
                        Interval = product.Product.Interval ?? 0,
                        IntervalUnit = product.Product.IntervalUnit?.Value ?? "month",
                        Description = product.Product.Description ?? string.Empty
                    });
                }
            }

            return plans;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            _logger.LogError("Failed to list products: {Error}", ex.Error);
            throw new InvalidOperationException("Failed to retrieve subscription plans from Maxio", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId,
        string planHandle,
        CancellationToken ct = default)
    {
        try
        {
            var customer = await GetOrCreateCustomerAsync(userId, ct);
            var maxioCustomerId = customer.Id ?? throw new InvalidOperationException("Failed to get customer ID");

            var subscriptionRef = $"{userId}-{Guid.NewGuid():N}";

            var existingSubscription = await _client.Customers.ListCustomerSubscriptions(
                (int)maxioCustomerId,
                ct: ct);

            if (existingSubscription.Any(s =>
                s.Subscription?.Reference == subscriptionRef ||
                (s.Subscription?.Product?.Handle == planHandle && s.Subscription.State?.Value == "active")))
            {
                throw new InvalidOperationException("An active subscription to this plan already exists");
            }

            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = maxioCustomerId,
                    ProductHandle = planHandle,
                    Reference = subscriptionRef,
                    DeferSignup = false
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(createRequest, ct: ct);
            var subscription = response.Subscription ?? throw new InvalidOperationException("Failed to create subscription");

            return new SubscriptionDto
            {
                Id = subscription.Id ?? 0,
                State = subscription.State?.Value ?? "unknown",
                PriceInCents = subscription.ProductPriceInCents ?? 0,
                NextBillingDate = subscription.NextAssessmentAt,
                Reference = subscription.Reference ?? string.Empty
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError("Failed to create subscription: {Error}", ex.Error);
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new InvalidOperationException(
                    $"Subscription creation failed: {string.Join(", ", errorList.Errors)}",
                    ex);
            }

            throw new InvalidOperationException("Failed to create subscription in Maxio", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            var customer = await GetOrCreateCustomerAsync(userId, ct);
            var maxioCustomerId = customer.Id ?? throw new InvalidOperationException("Failed to get customer ID");

            var subscriptions = new List<SubscriptionDto>();
            var maxioSubs = await _client.Customers.ListCustomerSubscriptions((int)maxioCustomerId, ct: ct);

            foreach (var sub in maxioSubs)
            {
                if (sub.Subscription != null)
                {
                    subscriptions.Add(new SubscriptionDto
                    {
                        Id = sub.Subscription.Id ?? 0,
                        State = sub.Subscription.State?.Value ?? "unknown",
                        PriceInCents = sub.Subscription.ProductPriceInCents ?? 0,
                        NextBillingDate = sub.Subscription.NextAssessmentAt,
                        Reference = sub.Subscription.Reference ?? string.Empty
                    });
                }
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to list subscriptions: {Error}", ex.Error.ReadAsString());
            throw new InvalidOperationException("Failed to retrieve subscriptions from Maxio", ex);
        }
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
            return existing.Customer ?? throw new InvalidOperationException("Customer lookup returned empty");
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return await CreateCustomerAsync(userId, ct);
            }

            _logger.LogError("Failed to read customer by reference: {Error}", ex.Error.ReadAsString());
            throw;
        }
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "eShop",
                    LastName = "Customer",
                    Email = $"{userId}@eshop.local",
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(createRequest, ct: ct);
            return response.Customer ?? throw new InvalidOperationException("Failed to create customer");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                _logger.LogError("Customer creation validation error: {Errors}", errorResponse.Errors);
            }

            throw new InvalidOperationException("Failed to create customer in Maxio", ex);
        }
    }
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public string Reference { get; set; } = string.Empty;
}
