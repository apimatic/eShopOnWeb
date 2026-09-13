using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioService : IMaxioService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{_options.ProductFamilyHandle}",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            return response.Select(pr => new SubscriptionPlanDto
            {
                Id = pr.Product?.Id,
                Name = pr.Product?.Name,
                Handle = pr.Product?.Handle,
                Description = pr.Product?.Description,
                PriceInCents = pr.Product?.PriceInCents,
                IntervalUnit = pr.Product?.IntervalUnit?.Value,
                Interval = pr.Product?.Interval
            }).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            _logger.LogError(ex, "Failed to list products for family {Handle}", _options.ProductFamilyHandle);
            if (ex.Error.TryGetRawError(out var raw))
                throw new InvalidOperationException($"Failed to retrieve subscription plans: HTTP {(int)raw.StatusCode} {raw.ReadAsString()}", ex);
            throw new InvalidOperationException("Failed to retrieve subscription plans", ex);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list products for family {Handle}", _options.ProductFamilyHandle);
            throw new InvalidOperationException($"Failed to retrieve subscription plans: HTTP {(int)ex.Error.StatusCode} {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Failed to list products for family {Handle}", _options.ProductFamilyHandle);
            throw new InvalidOperationException($"Failed to retrieve subscription plans: {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionResultDto> SubscribeAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        string productHandle,
        CancellationToken ct = default)
    {
        var customerId = await EnsureCustomerExistsAsync(userId, email, firstName, lastName, ct);

        try
        {
            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    PaymentCollectionMethod = CollectionMethod.Invoice
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(subscriptionRequest, ct);
            var sub = response.Subscription;

            return new SubscriptionResultDto
            {
                SubscriptionId = sub?.Id,
                State = sub?.State?.Value,
                PlanName = sub?.Product?.Name,
                PlanHandle = sub?.Product?.Handle,
                PriceInCents = sub?.ProductPriceInCents,
                NextBillingDate = sub?.CurrentPeriodEndsAt,
                CreatedAt = sub?.CreatedAt,
                CustomerId = customerId
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError(ex, "Failed to create subscription for customer {CustomerId}", customerId);
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
                throw new InvalidOperationException($"Subscription creation rejected: {string.Join(", ", errorList.Errors ?? Array.Empty<string>())}", ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw new InvalidOperationException($"Failed to create subscription: HTTP {(int)raw.StatusCode} {raw.ReadAsString()}", ex);
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetailDto>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        int? customerId = await FindCustomerIdByReferenceAsync(userId, ct);
        if (customerId == null)
            return Array.Empty<SubscriptionDetailDto>();

        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId.Value, ct);

            return response.Select(sr => new SubscriptionDetailDto
            {
                SubscriptionId = sr.Subscription?.Id,
                State = sr.Subscription?.State?.Value,
                PlanName = sr.Subscription?.Product?.Name,
                PlanHandle = sr.Subscription?.Product?.Handle,
                PriceInCents = sr.Subscription?.ProductPriceInCents,
                NextBillingDate = sr.Subscription?.CurrentPeriodEndsAt,
                NextAssessmentAt = sr.Subscription?.NextAssessmentAt,
                CreatedAt = sr.Subscription?.CreatedAt,
                ActivatedAt = sr.Subscription?.ActivatedAt,
                Currency = sr.Subscription?.Currency,
                CancelAtEndOfPeriod = sr.Subscription?.CancelAtEndOfPeriod
            }).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions for customer {CustomerId}", customerId);
            throw new InvalidOperationException($"Failed to retrieve subscriptions: HTTP {(int)ex.Error.StatusCode}", ex);
        }
    }

    private async Task<int> EnsureCustomerExistsAsync(
        string userId, string email, string firstName, string lastName, CancellationToken ct)
    {
        var existingId = await FindCustomerIdByReferenceAsync(userId, ct);
        if (existingId != null)
            return existingId.Value;

        try
        {
            var request = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(request, ct);
            return response.Customer?.Id ?? throw new InvalidOperationException("Created customer but ID was null");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                _logger.LogInformation("Customer already exists for reference {Reference}, looking up", userId);
                var retryId = await FindCustomerIdByReferenceAsync(userId, ct);
                if (retryId != null)
                    return retryId.Value;
            }
            if (ex.Error.TryGetRawError(out var raw))
                throw new InvalidOperationException($"Failed to create customer: HTTP {(int)raw.StatusCode} {raw.ReadAsString()}", ex);
            throw new InvalidOperationException("Failed to create customer", ex);
        }
    }

    private async Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            throw;
        }
    }
}
