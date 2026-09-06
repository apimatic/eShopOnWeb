using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IConfiguration configuration,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SubscriptionPlanDto[]> ListAvailablePlansAsync(CancellationToken ct = default)
    {
        try
        {
            var familyHandle = _configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";

            var allProducts = await _client.Products.ListProducts(
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
            foreach (var productResponse in allProducts)
            {
                var product = productResponse.Product;
                if (product != null && product.ProductFamily?.Handle == familyHandle)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Handle = product.Handle ?? "",
                        Name = product.Name ?? "",
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 1,
                        IntervalUnit = product.IntervalUnit?.Value ?? "month"
                    });
                }
            }

            return plans.ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list subscription plans. Status: {Status}",
                ex.Error.StatusCode);
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateOrGetCustomerAndSubscribeAsync(
        string userId,
        string userEmail,
        string userFirstName,
        string userLastName,
        string productHandle,
        CancellationToken ct = default)
    {
        try
        {
            int customerId = await EnsureCustomerExistsAsync(userId, userEmail, userFirstName, userLastName, ct);

            var subscription = await CreateSubscriptionAsync(customerId, productHandle, ct);

            return new SubscriptionDto
            {
                Id = subscription.Id ?? 0,
                State = subscription.State?.Value ?? "unknown",
                ActivatedAt = subscription.ActivatedAt,
                CurrentPeriodStartsAt = subscription.CurrentPeriodStartedAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError(ex, "Failed to create subscription for customer {UserId}", userId);
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                _logger.LogError("Subscription errors: {Errors}", string.Join(", ", errorList.Errors ?? new List<string>()));
            }
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription. Status: {Status}", ex.Error.StatusCode);
            throw;
        }
    }

    public async Task<SubscriptionDto[]> GetUserSubscriptionsAsync(
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            var customerId = await GetCustomerIdByReferenceAsync(userId, ct);
            if (customerId == null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId.Value,
                ct: ct);

            return subscriptions
                .Select(sr => new SubscriptionDto
                {
                    Id = sr.Subscription?.Id ?? 0,
                    State = sr.Subscription?.State?.Value ?? "unknown",
                    ActivatedAt = sr.Subscription?.ActivatedAt,
                    CurrentPeriodStartsAt = sr.Subscription?.CurrentPeriodStartedAt,
                    CurrentPeriodEndsAt = sr.Subscription?.CurrentPeriodEndsAt,
                    NextAssessmentAt = sr.Subscription?.NextAssessmentAt
                })
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions for customer {UserId}. Status: {Status}",
                userId, ex.Error.StatusCode);
            throw;
        }
    }

    private async Task<int> EnsureCustomerExistsAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        CancellationToken ct)
    {
        var existingCustomerId = await GetCustomerIdByReferenceAsync(userId, ct);
        if (existingCustomerId.HasValue)
        {
            return existingCustomerId.Value;
        }

        var newCustomer = await CreateCustomerAsync(userId, email, firstName, lastName, ct);
        return newCustomer.Id ?? throw new InvalidOperationException("Customer creation failed - no ID returned");
    }

    private async Task<int?> GetCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: reference,
                ct: ct);

            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Customer not found for reference {Reference}", reference);
                return null;
            }

            _logger.LogError(ex, "Error looking up customer by reference {Reference}. Status: {Status}",
                reference, ex.Error.StatusCode);
            throw;
        }
    }

    private async Task<Customer> CreateCustomerAsync(
        string reference,
        string email,
        string firstName,
        string lastName,
        CancellationToken ct)
    {
        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    Reference = reference,
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: createRequest,
                ct: ct);

            return response.Customer ?? throw new InvalidOperationException("Customer creation returned no customer");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            _logger.LogError(ex, "Failed to create customer with reference {Reference}", reference);
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                _logger.LogError("Customer creation errors: {Errors}", errorResponse.Errors);
            }
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Unexpected error creating customer. Status: {Status}", ex.Error.StatusCode);
            throw;
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken ct)
    {
        try
        {
            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: createRequest,
                ct: ct);

            return response.Subscription ?? throw new InvalidOperationException("Subscription creation returned no subscription");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError(ex, "Failed to create subscription for customer {CustomerId}", customerId);
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errors = string.Join(", ", errorList.Errors ?? new List<string>());
                _logger.LogError("Subscription errors: {Errors}", errors);
            }
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription. Status: {Status}", ex.Error.StatusCode);
            throw;
        }
    }
}

public class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = "";
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
