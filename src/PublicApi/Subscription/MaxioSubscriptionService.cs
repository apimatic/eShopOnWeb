using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioCustomer = MaxioAdvancedBilling.Models.Customer;
using MaxioSubscriptionModel = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.PublicApi.Subscription;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(
        IOptions<MaxioConfiguration> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _config = options.Value;
        _logger = logger;
        _productFamilyHandle = _config.ProductFamilyHandle ?? "eshop-subscribe";
        _client = InitializeClient();
    }

    private MaxioAdvancedBillingClient InitializeClient()
    {
        var apiKey = _config.ApiKey;
        var subdomain = _config.Subdomain;
        var baseUrl = _config.BaseUrl;

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(subdomain))
        {
            throw new InvalidOperationException("Maxio:ApiKey and Maxio:Subdomain must be configured");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = apiKey,
                Password = "x"
            },
            Environment = ServerEnvironment.Us
        };

        if (!string.IsNullOrEmpty(baseUrl))
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = subdomain;
        }

        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Fetching subscription plans from Maxio");

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

            var plans = response
                .Where(pr => pr.Product != null && pr.Product.ArchivedAt == null)
                .Select(pr => new SubscriptionPlanDto
                {
                    Handle = pr.Product!.Handle ?? string.Empty,
                    Name = pr.Product.Name ?? string.Empty,
                    Description = pr.Product.Description ?? string.Empty,
                    PriceInCents = pr.Product.PriceInCents ?? 0,
                    IntervalUnit = pr.Product.IntervalUnit?.Value ?? "month",
                    Interval = pr.Product.Interval ?? 1
                })
                .ToList();

            _logger.LogInformation("Successfully fetched {PlanCount} plans", plans.Count);
            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list products: {StatusCode}", (int)ex.Error.StatusCode);
            throw new MaxioServiceException("Failed to retrieve subscription plans", ex);
        }
    }

    public async Task<CustomerSubscriptionDto> CreateOrUpdateSubscriptionAsync(
        string userId,
        string userEmail,
        string planHandle,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Processing subscription for user {UserId} with plan {PlanHandle}", userId, planHandle);

            var customer = await EnsureCustomerExistsAsync(userId, userEmail, ct);
            _logger.LogInformation("Customer {CustomerId} ready for subscription", customer.Id);

            var subscription = await CreateSubscriptionAsync(customer.Id ?? 0, planHandle, ct);
            _logger.LogInformation("Subscription {SubscriptionId} created for customer {CustomerId}",
                subscription.Id, customer.Id);

            return new CustomerSubscriptionDto
            {
                SubscriptionId = subscription.Id ?? 0,
                CustomerId = customer.Id ?? 0,
                State = subscription.State?.Value ?? "unknown",
                ProductHandle = planHandle,
                PriceInCents = subscription.ProductPriceInCents ?? 0,
                NextBillingDate = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt
            };
        }
        catch (MaxioServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            throw new MaxioServiceException("Failed to create subscription", ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscriptionDto>> GetCustomerSubscriptionsAsync(
        string userId,
        string userEmail,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Fetching subscriptions for user {UserId}", userId);

            var customer = await FindCustomerByReferenceAsync(userId, ct);
            if (customer == null)
            {
                _logger.LogInformation("No customer found for user {UserId}", userId);
                return new List<CustomerSubscriptionDto>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customer.Id ?? 0,
                ct: ct);

            var result = subscriptions
                .Select(sr => new CustomerSubscriptionDto
                {
                    SubscriptionId = sr.Subscription?.Id ?? 0,
                    CustomerId = customer.Id ?? 0,
                    State = sr.Subscription?.State?.Value ?? "unknown",
                    ProductHandle = sr.Subscription?.Product?.Handle ?? string.Empty,
                    PriceInCents = sr.Subscription?.ProductPriceInCents ?? 0,
                    NextBillingDate = sr.Subscription?.NextAssessmentAt,
                    ActivatedAt = sr.Subscription?.ActivatedAt,
                    CreatedAt = sr.Subscription?.CreatedAt
                })
                .ToList();

            _logger.LogInformation("Retrieved {SubscriptionCount} subscriptions for user {UserId}",
                result.Count, userId);
            return result;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list customer subscriptions: {StatusCode}", (int)ex.Error.StatusCode);
            throw new MaxioServiceException("Failed to retrieve subscriptions", ex);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerExistsAsync(
        string userId,
        string userEmail,
        CancellationToken ct)
    {
        var existing = await FindCustomerByReferenceAsync(userId, ct);
        if (existing != null)
        {
            _logger.LogInformation("Customer already exists: {CustomerId}", existing.Id);
            return existing;
        }

        return await CreateCustomerAsync(userId, userEmail, ct);
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Looking up customer by reference: {Reference}", reference);
            var response = await _client.Customers.ReadCustomerByReference(
                reference: reference,
                ct: ct);

            _logger.LogDebug("Customer found: {CustomerId}", response.Customer?.Id);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Customer not found by reference: {Reference}", reference);
                return null;
            }

            _logger.LogError(ex, "Error looking up customer: {StatusCode}", (int)ex.Error.StatusCode);
            throw;
        }
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(
        string reference,
        string email,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Creating new customer with reference: {Reference}", reference);

            var parts = email.Split('@');
            var firstName = parts[0];
            var lastName = "User";

            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: body,
                ct: ct);

            _logger.LogInformation("Customer created successfully: {CustomerId}", response.Customer?.Id);
            return response.Customer ?? throw new MaxioServiceException("Customer creation returned null");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                _logger.LogError("Customer creation failed with validation errors");
                throw new MaxioServiceException($"Customer creation failed: {string.Join(", ", errorResponse.Errors?.PerPage ?? new List<string>())}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError(ex, "Customer creation failed: {StatusCode}", (int)rawError.StatusCode);
                throw new MaxioServiceException("Customer creation failed", ex);
            }
            throw;
        }
    }

    private async Task<MaxioSubscriptionModel> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Creating subscription for customer {CustomerId} with product {ProductHandle}",
                customerId, productHandle);

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: body,
                ct: ct);

            _logger.LogInformation("Subscription created successfully: {SubscriptionId}", response.Subscription?.Id);
            return response.Subscription ?? throw new MaxioServiceException("Subscription creation returned null");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errors = errorList.Errors ?? new List<string>();
                _logger.LogError("Subscription creation failed: {Errors}", string.Join("; ", errors));
                throw new MaxioServiceException($"Subscription creation failed: {string.Join("; ", errors)}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError(ex, "Subscription creation failed: {StatusCode}", (int)rawError.StatusCode);
                throw new MaxioServiceException("Subscription creation failed", ex);
            }
            throw;
        }
    }
}

public class MaxioServiceException : Exception
{
    public MaxioServiceException(string message) : base(message) { }
    public MaxioServiceException(string message, Exception innerException)
        : base(message, innerException) { }
}
