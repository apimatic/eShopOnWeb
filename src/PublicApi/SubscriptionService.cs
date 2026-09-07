using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Errors;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;
using CreateSubscriptionRequestDto = MaxioAdvancedBilling.Models.CreateSubscriptionRequest;

namespace Microsoft.eShopWeb.PublicApi;

public class SubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioAdvancedBillingClient client, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: "eshop-subscribe",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            var plans = response
                .Where(r => r.Product != null)
                .Select(r => r.Product!)
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id ?? 0,
                    Handle = p.Handle ?? "",
                    Name = p.Name ?? "",
                    PriceInCents = p.PriceInCents ?? 0,
                    Interval = p.Interval ?? 1,
                    IntervalUnit = p.IntervalUnit?.Value ?? "month"
                })
                .ToList();

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to fetch subscription plans: {ex.Error.ReadAsString()}");
            throw new InvalidOperationException("Failed to fetch subscription plans", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to deserialize subscription plans response: {ex.Message}");
            throw new InvalidOperationException("Failed to process subscription plans response", ex);
        }
    }

    public async Task<int> GetOrCreateMaxioCustomerAsync(string userId, string? email, string? firstName, string? lastName, CancellationToken ct = default)
    {
        try
        {
            // Try to fetch existing customer by reference
            try
            {
                var existingCustomer = await _client.Customers.ReadCustomerByReference(
                    reference: userId,
                    ct: ct);

                if (existingCustomer.Customer?.Id.HasValue == true)
                {
                    return existingCustomer.Customer.Id.Value;
                }
            }
            catch (SdkException<RawError> ex)
            {
                // 404 means customer doesn't exist, which is expected on first subscription
                if (ex.Error.StatusCode != HttpStatusCode.NotFound)
                {
                    throw;
                }
            }

            // Create new customer
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName ?? "Customer",
                    LastName = lastName ?? userId,
                    Email = email,
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: createRequest,
                ct: ct);

            if (response.Customer?.Id.HasValue == true)
            {
                return response.Customer.Id.Value;
            }

            throw new InvalidOperationException("Customer creation returned no ID");
        }
        catch (SdkException<CreateCustomerError?> ex)
        {
            if (ex.Error != null && ex.Error.TryGetCustomerErrorResponse1(out var validationError))
            {
                _logger.LogError($"Customer creation validation failed");
                throw new InvalidOperationException("Customer validation error", ex);
            }
            else if (ex.Error != null && ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError($"Customer creation error: {rawError.ReadAsString()}");
                throw new InvalidOperationException("Failed to create customer", ex);
            }
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to deserialize customer response: {ex.Message}");
            throw new InvalidOperationException("Failed to process customer response", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string planHandle, string userId, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new CreateSubscriptionRequestDto
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    Reference = $"{userId}-{planHandle}-{DateTime.UtcNow.Ticks}"
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: createRequest,
                ct: ct);

            if (response.Subscription == null)
            {
                throw new InvalidOperationException("Subscription creation returned no data");
            }

            var subscription = response.Subscription;
            return new SubscriptionDto
            {
                Id = subscription.Id ?? 0,
                State = subscription.State?.Value ?? "unknown",
                ProductName = subscription.Product?.Name ?? "",
                ProductHandle = subscription.Product?.Handle ?? planHandle,
                PriceInCents = subscription.ProductPriceInCents ?? 0,
                NextBillingDate = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt,
                ActivatedAt = subscription.ActivatedAt
            };
        }
        catch (SdkException<CreateSubscriptionError?> ex)
        {
            if (ex.Error != null && ex.Error.TryGetErrorListResponse1(out var validationError))
            {
                _logger.LogError($"Subscription creation validation failed");
                throw new InvalidOperationException("Subscription validation error", ex);
            }
            else if (ex.Error != null && ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError($"Subscription creation error: {rawError.ReadAsString()}");
                throw new InvalidOperationException("Failed to create subscription", ex);
            }
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to deserialize subscription response: {ex.Message}");
            throw new InvalidOperationException("Failed to process subscription response", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListUserSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            var subscriptions = response
                .Where(r => r.Subscription != null)
                .Select(r => r.Subscription!)
                .Select(s => new SubscriptionDto
                {
                    Id = s.Id ?? 0,
                    State = s.State?.Value ?? "unknown",
                    ProductName = s.Product?.Name ?? "",
                    ProductHandle = s.Product?.Handle ?? "",
                    PriceInCents = s.ProductPriceInCents ?? 0,
                    NextBillingDate = s.NextAssessmentAt,
                    CreatedAt = s.CreatedAt,
                    ActivatedAt = s.ActivatedAt
                })
                .ToList();

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to fetch subscriptions: {ex.Error.ReadAsString()}");
            throw new InvalidOperationException("Failed to fetch subscriptions", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to deserialize subscriptions response: {ex.Message}");
            throw new InvalidOperationException("Failed to process subscriptions response", ex);
        }
    }
}
