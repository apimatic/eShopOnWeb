using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<MaxioProductPlan>> GetAvailablePlansAsync(CancellationToken ct = default);
    Task<int> EnsureCustomerExistsAsync(string userId, string email, CancellationToken ct = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string planHandle, string subscriptionReference, CancellationToken ct = default);
    Task<List<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        ILogger<MaxioSubscriptionService> logger,
        string productFamilyHandle)
    {
        _client = client;
        _logger = logger;
        _productFamilyHandle = productFamilyHandle;
    }

    public async Task<IReadOnlyList<MaxioProductPlan>> GetAvailablePlansAsync(CancellationToken ct = default)
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
                perPage: 20,
                ct: ct);

            var plans = new List<MaxioProductPlan>();
            foreach (var item in response)
            {
                var product = item.Product;
                if (product != null && product.Handle != null && product.Interval.HasValue)
                {
                    plans.Add(new MaxioProductPlan
                    {
                        Id = (int)(product.Id ?? 0),
                        Handle = product.Handle,
                        Name = product.Name ?? string.Empty,
                        Description = product.Description,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = (int)product.Interval.Value,
                        IntervalUnit = product.IntervalUnit?.Value ?? "month"
                    });
                }
            }

            _logger.LogInformation("Retrieved {Count} subscription plans from Maxio", plans.Count);
            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list products from Maxio. Status: {Status}", (int?)ex.Error.StatusCode);
            throw;
        }
    }

    public async Task<int> EnsureCustomerExistsAsync(string userId, string email, CancellationToken ct = default)
    {
        try
        {
            var readResponse = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);

            _logger.LogInformation("Customer already exists for userId {UserId}, customerId: {CustomerId}", userId, readResponse.Customer?.Id);
            return (int)(readResponse.Customer?.Id ?? 0);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Customer not found for userId {UserId}, creating new customer", userId);
                return await CreateCustomerAsync(userId, email, ct);
            }

            _logger.LogError(ex, "Failed to read customer from Maxio. Status: {Status}", (int?)ex.Error.StatusCode);
            throw;
        }
    }

    private async Task<int> CreateCustomerAsync(string reference, string email, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    Email = email,
                    Reference = reference,
                    FirstName = "Customer",
                    LastName = reference
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: createRequest,
                ct: ct);

            var customerId = (int)(response.Customer?.Id ?? 0);
            _logger.LogInformation("Created new customer in Maxio for userId {UserId}, customerId: {CustomerId}", reference, customerId);
            return customerId;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validationError))
            {
                var errors = new List<string>();
                if (validationError.Errors?.PerPage != null)
                    errors.AddRange(validationError.Errors.PerPage);
                if (validationError.Errors?.PricePoint != null)
                    errors.AddRange(validationError.Errors.PricePoint);
                var errorMsg = string.Join(", ", errors.Any() ? errors : new List<string> { "Unknown error" });
                _logger.LogError("Failed to create customer: {Error}", errorMsg);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError("Failed to create customer. Status: {Status}, Body: {Body}", (int?)rawError.StatusCode, rawError.ReadAsString());
            }
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string planHandle, string subscriptionReference, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = CollectionMethod.Prepaid,
                    Reference = subscriptionReference
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: createRequest,
                ct: ct);

            var subscription = response.Subscription;
            _logger.LogInformation("Created subscription {SubscriptionId} for customerId {CustomerId}, state: {State}",
                subscription?.Id, customerId, subscription?.State?.Value);

            return new SubscriptionDto
            {
                Id = (int)(subscription?.Id ?? 0),
                State = subscription?.State?.Value ?? "unknown",
                PlanHandle = planHandle,
                PlanName = subscription?.Product?.Name ?? string.Empty,
                PricePerMonth = subscription?.Product?.PriceInCents.HasValue == true ? subscription.Product.PriceInCents.Value / 100m : 0,
                NextAssessmentAt = subscription?.NextAssessmentAt,
                CurrentPeriodEndsAt = subscription?.CurrentPeriodEndsAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var fieldErrors))
            {
                var errorMsg = string.Join(", ", fieldErrors.Errors ?? Enumerable.Empty<string>());
                _logger.LogError("Failed to create subscription: {Error}", errorMsg);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError("Failed to create subscription. Status: {Status}, Body: {Body}", (int?)rawError.StatusCode, rawError.ReadAsString());
            }
            throw;
        }
        catch (System.Text.Json.JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to deserialize subscription response from Maxio");
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            var subscriptions = new List<SubscriptionDto>();
            foreach (var response in responses)
            {
                var subscription = response.Subscription;
                if (subscription != null)
                {
                    subscriptions.Add(new SubscriptionDto
                    {
                        Id = (int)(subscription.Id ?? 0),
                        State = subscription.State?.Value ?? "unknown",
                        PlanHandle = subscription.Product?.Handle ?? string.Empty,
                        PlanName = subscription.Product?.Name ?? string.Empty,
                        PricePerMonth = subscription.Product?.PriceInCents.HasValue == true ? subscription.Product.PriceInCents.Value / 100m : 0,
                        NextAssessmentAt = subscription.NextAssessmentAt,
                        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
                    });
                }
            }

            _logger.LogInformation("Retrieved {Count} subscriptions for customerId {CustomerId}", subscriptions.Count, customerId);
            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions from Maxio. Status: {Status}", (int?)ex.Error.StatusCode);
            throw;
        }
    }
}

public class MaxioProductPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
