using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<MaxioSubscriptionService> logger)
    {
        _logger = logger;

        var maxioConfig = configuration.GetSection("Maxio");
        var apiKey = maxioConfig["ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey is required");
        var subdomain = maxioConfig["Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain is required");
        _productFamilyHandle = maxioConfig["ProductFamilyHandle"] ?? throw new InvalidOperationException("Maxio:ProductFamilyHandle is required");

        var httpClient = httpClientFactory.CreateClient();
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = apiKey,
                Password = "x"
            }
        };

        options.Server.Production.Us.Site = subdomain;

        var baseUrl = maxioConfig["BaseUrl"];
        if (!string.IsNullOrEmpty(baseUrl))
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
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
                if (productResponse.Product == null)
                    continue;

                var product = productResponse.Product;

                if (!string.IsNullOrEmpty(product.ProductFamily?.Handle) &&
                    product.ProductFamily.Handle == _productFamilyHandle)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Handle = product.Handle ?? string.Empty,
                        Name = product.Name ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        IntervalUnit = product.IntervalUnit?.Value ?? "month",
                        Interval = product.Interval ?? 1
                    });
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing subscription plans from Maxio. Status: {StatusCode}", ex.Error.StatusCode);
            throw new MaxioServiceException("Failed to retrieve subscription plans", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscription plans");
            throw new MaxioServiceException("Unexpected error retrieving subscription plans", ex);
        }
    }

    public async Task<CustomerDto> GetOrCreateCustomerAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        CancellationToken ct = default)
    {
        try
        {
            var customer = await ReadCustomerByReferenceAsync(userId, ct);
            return customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Customer not found for reference {Reference}, creating new customer", userId);
            return await CreateCustomerAsync(userId, firstName, lastName, email, ct);
        }
    }

    private async Task<CustomerDto> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);

            if (response.Customer == null)
                throw new MaxioServiceException("Customer response missing customer data");

            return new CustomerDto
            {
                Id = response.Customer.Id ?? 0,
                FirstName = response.Customer.FirstName ?? string.Empty,
                LastName = response.Customer.LastName ?? string.Empty,
                Email = response.Customer.Email ?? string.Empty,
                Reference = response.Customer.Reference ?? string.Empty
            };
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error reading customer by reference. Status: {StatusCode}", ex.Error.StatusCode);
            throw new MaxioServiceException($"Failed to read customer: {ex.Error.StatusCode}", ex);
        }
    }

    private async Task<CustomerDto> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken ct)
    {
        try
        {
            var request = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            };

            var response = await _client.Customers.CreateCustomer(body: request, ct: ct);

            if (response.Customer == null)
                throw new MaxioServiceException("Customer creation response missing customer data");

            return new CustomerDto
            {
                Id = response.Customer.Id ?? 0,
                FirstName = response.Customer.FirstName ?? string.Empty,
                LastName = response.Customer.LastName ?? string.Empty,
                Email = response.Customer.Email ?? string.Empty,
                Reference = response.Customer.Reference ?? string.Empty
            };
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validationError))
            {
                var errorList = new List<string>();
                if (validationError.Errors?.PerPage != null)
                    errorList.AddRange(validationError.Errors.PerPage);
                if (validationError.Errors?.PricePoint != null)
                    errorList.AddRange(validationError.Errors.PricePoint);

                var errors = string.Join("; ", errorList.Any() ? errorList : new[] { "Validation error" });
                _logger.LogWarning("Customer creation validation error: {Errors}", errors);
                throw new MaxioServiceException($"Customer validation error: {errors}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError(ex, "Customer creation raw error. Status: {StatusCode}", rawError.StatusCode);
                throw new MaxioServiceException($"Failed to create customer: {rawError.StatusCode}", ex);
            }
            throw new MaxioServiceException("Failed to create customer", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating customer");
            throw new MaxioServiceException("Unexpected error creating customer", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        string planHandle,
        CancellationToken ct = default)
    {
        try
        {
            var customer = await GetOrCreateCustomerAsync(userId, firstName, lastName, email, ct);

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = (int)customer.Id,
                    ProductHandle = planHandle,
                    Reference = $"{userId}_{planHandle}_{DateTimeOffset.UtcNow.Ticks}"
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: request, ct: ct);

            if (response.Subscription == null)
                throw new MaxioServiceException("Subscription creation response missing subscription data");

            return MapSubscriptionToDto(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationError))
            {
                var errors = string.Join("; ", validationError.Errors ?? Array.Empty<string>());
                _logger.LogWarning("Subscription creation validation error: {Errors}", errors);
                throw new MaxioServiceException($"Subscription validation error: {errors}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError(ex, "Subscription creation raw error. Status: {StatusCode}", rawError.StatusCode);
                throw new MaxioServiceException($"Failed to create subscription: {rawError.StatusCode}", ex);
            }
            throw new MaxioServiceException("Failed to create subscription", ex);
        }
        catch (MaxioServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            throw new MaxioServiceException("Unexpected error creating subscription", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
        long customerId,
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

            var subscriptions = new List<SubscriptionDto>();
            foreach (var subResponse in response)
            {
                if (subResponse.Subscription == null)
                    continue;

                // Filter by customer ID - compare nested Customer.Id with customerId parameter
                if (subResponse.Subscription.Customer?.Id == (int)customerId)
                {
                    subscriptions.Add(MapSubscriptionToDto(subResponse.Subscription));
                }
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing subscriptions. Status: {StatusCode}", ex.Error.StatusCode);
            throw new MaxioServiceException("Failed to retrieve subscriptions", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions");
            throw new MaxioServiceException("Unexpected error retrieving subscriptions", ex);
        }
    }

    private static SubscriptionDto MapSubscriptionToDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.Value ?? "unknown",
            CustomerId = subscription.Customer?.Id ?? 0,
            ProductId = subscription.Product?.Id ?? 0,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents ?? 0
        };
    }
}

public class MaxioServiceException : Exception
{
    public MaxioServiceException(string message) : base(message) { }
    public MaxioServiceException(string message, Exception innerException) : base(message, innerException) { }
}

public class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }

    public decimal GetMonthlyPrice() => PriceInCents / 100m;
}

public class CustomerDto
{
    public long Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public long CurrentBillingAmountInCents { get; set; }

    public decimal GetCurrentBillingAmount() => CurrentBillingAmountInCents / 100m;
}
