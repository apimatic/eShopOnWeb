using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioService : IMaxioService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyHandle,
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

            return products.Select(p => new SubscriptionPlanDto
            {
                Id = (int)(p.Product.Id ?? 0),
                Name = p.Product.Name ?? string.Empty,
                Handle = p.Product.Handle ?? string.Empty,
                Description = p.Product.Description ?? string.Empty,
                PriceInCents = (long)(p.Product.PriceInCents ?? 0),
                Interval = (int)(p.Product.Interval ?? 1),
                IntervalUnit = p.Product.IntervalUnit?.Value ?? "month",
                RequireCreditCard = p.Product.RequireCreditCard ?? false
            }).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            _logger.LogError(ex, "Failed to list Maxio subscription plans");
            throw new InvalidOperationException($"Failed to list subscription plans: {DescribeRaw(ex.Error)}", ex);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list Maxio subscription plans");
            throw new InvalidOperationException($"Failed to list subscription plans: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Maxio subscription plans response");
            throw new InvalidOperationException($"Failed to list subscription plans: Maxio returned an unexpected response. Check that the product family handle '{_settings.ProductFamilyHandle}' is correct.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing Maxio subscription plans");
            throw new InvalidOperationException($"Failed to list subscription plans: {ex.Message}", ex);
        }
    }

    public async Task<int> EnsureCustomerExistsAsync(
        string userId, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(userId, ct);
            return (int)(existing.Customer.Id ?? throw new InvalidOperationException("Customer has no ID"));
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Customer does not exist — create
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to look up Maxio customer by reference");
            throw new InvalidOperationException($"Failed to look up customer: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error looking up Maxio customer");
            throw new InvalidOperationException($"Failed to look up customer: {ex.Message}", ex);
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = userId
                    }
                }, ct);

            return (int)(created.Customer.Id ?? throw new InvalidOperationException("Created customer has no ID"));
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            _logger.LogError(ex, "Failed to create Maxio customer");
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                throw new InvalidOperationException($"Failed to create customer: {errorResponse.Errors}", ex);
            }
            throw new InvalidOperationException($"Failed to create customer: {DescribeRaw(ex.Error)}", ex);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to create Maxio customer");
            throw new InvalidOperationException($"Failed to create customer: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error creating Maxio customer");
            throw new InvalidOperationException($"Failed to create customer: {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionResultDto> CreateSubscriptionAsync(
        int customerId, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customerId
                    }
                }, ct);

            var sub = response.Subscription ?? throw new InvalidOperationException("Subscription response had no subscription");

            return new SubscriptionResultDto
            {
                SubscriptionId = (int)(sub.Id ?? 0),
                State = sub.State?.Value ?? "unknown",
                PriceInCents = (long)(sub.ProductPriceInCents ?? 0),
                NextBillingAt = sub.NextAssessmentAt,
                ActivatedAt = sub.ActivatedAt,
                ProductName = sub.Product?.Name ?? string.Empty
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError(ex, "Failed to create Maxio subscription");
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errors = string.Join("; ", errorList.Errors ?? Array.Empty<string>());
                throw new InvalidOperationException($"Failed to create subscription: {errors}", ex);
            }
            throw new InvalidOperationException($"Failed to create subscription: {DescribeRaw(ex.Error)}", ex);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to create Maxio subscription");
            throw new InvalidOperationException($"Failed to create subscription: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error creating Maxio subscription");
            throw new InvalidOperationException($"Failed to create subscription: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetailDto>> ListMySubscriptionsAsync(
        int maxioCustomerId, CancellationToken ct = default)
    {
        try
        {
            var subs = await _client.Customers.ListCustomerSubscriptions(maxioCustomerId, ct);

            return subs.Select(s => new SubscriptionDetailDto
            {
                SubscriptionId = (int)(s.Subscription?.Id ?? 0),
                State = s.Subscription?.State?.Value ?? "unknown",
                PriceInCents = (long)(s.Subscription?.ProductPriceInCents ?? 0),
                NextBillingAt = s.Subscription?.NextAssessmentAt,
                CurrentPeriodEndsAt = s.Subscription?.CurrentPeriodEndsAt,
                ActivatedAt = s.Subscription?.ActivatedAt,
                ProductName = s.Subscription?.Product?.Name ?? string.Empty,
                ProductHandle = s.Subscription?.Product?.Handle ?? string.Empty
            }).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to list Maxio subscriptions for customer {CustomerId}", maxioCustomerId);
            throw new InvalidOperationException($"Failed to list subscriptions: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error listing Maxio subscriptions for customer {CustomerId}", maxioCustomerId);
            throw new InvalidOperationException($"Failed to list subscriptions: {ex.Message}", ex);
        }
    }

    private static string DescribeRaw(RawError raw)
    {
        return $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}";
    }

    private static string DescribeRaw(ApiError error)
    {
        if (error.TryGetRawError(out var raw))
        {
            return $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}";
        }
        return error.ToString() ?? "Unknown error";
    }
}
