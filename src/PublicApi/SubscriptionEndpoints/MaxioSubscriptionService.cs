using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, string productFamilyHandle)
    {
        _client = client;
        _productFamilyHandle = productFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _productFamilyHandle,
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
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var msg))
                throw new InvalidOperationException($"Failed to list plans: {msg}");
            if (ex.Error.TryGetRawError(out var raw))
                throw new InvalidOperationException($"Failed to list plans: HTTP {(int)raw.StatusCode}");
            throw new InvalidOperationException("Failed to list plans.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to list plans: HTTP {(int)ex.Error.StatusCode} {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to list plans: {ex.Message}", ex);
        }

        return products
            .Where(p => p.Product != null)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Product.Id ?? 0,
                Name = p.Product.Name ?? string.Empty,
                Handle = p.Product.Handle ?? string.Empty,
                Description = p.Product.Description ?? string.Empty,
                PriceInCents = p.Product.PriceInCents ?? 0,
                Interval = p.Product.Interval ?? 0,
                IntervalUnit = p.Product.IntervalUnit?.Value ?? "month"
            })
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        string productHandle,
        CancellationToken ct = default)
    {
        var customerId = await EnsureCustomerExistsAsync(userId, email, firstName, lastName, ct);

        var response = await CreateSubscriptionForCustomerAsync(customerId, productHandle, ct);

        return MapSubscription(response);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        CancellationToken ct = default)
    {
        var customerId = await EnsureCustomerExistsAsync(userId, email, firstName, lastName, ct);

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to list subscriptions: HTTP {(int)ex.Error.StatusCode}", ex);
        }

        return subscriptions
            .Where(s => s.Subscription != null)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<int> EnsureCustomerExistsAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        CancellationToken ct)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(userId, ct);
            return existing.Customer.Id ?? throw new InvalidOperationException("Customer ID is null.");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Customer not found — create one
        }

        var createResponse = await _client.Customers.CreateCustomer(
            new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            },
            ct);

        return createResponse.Customer.Id ?? throw new InvalidOperationException("Created customer ID is null.");
    }

    private async Task<SubscriptionResponse> CreateSubscriptionForCustomerAsync(
        int customerId,
        string productHandle,
        CancellationToken ct)
    {
        try
        {
            return await _client.Subscriptions.CreateSubscription(
                new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customerId
                    }
                },
                ct);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
                throw new InvalidOperationException($"Failed to create subscription: {errorList}");
            if (ex.Error.TryGetRawError(out var raw))
                throw new InvalidOperationException($"Failed to create subscription: HTTP {(int)raw.StatusCode} {raw.ReadAsString()}");
            throw new InvalidOperationException("Failed to create subscription.", ex);
        }
    }

    private static SubscriptionDto MapSubscription(SubscriptionResponse response)
    {
        var sub = response.Subscription;
        return new SubscriptionDto
        {
            Id = sub?.Id ?? 0,
            State = sub?.State?.Value ?? "unknown",
            PlanName = sub?.Product?.Name ?? string.Empty,
            PlanHandle = sub?.Product?.Handle ?? string.Empty,
            PriceInCents = sub?.Product?.PriceInCents ?? 0,
            NextBillingAt = sub?.NextAssessmentAt,
            ActivatedAt = sub?.ActivatedAt,
            CreatedAt = sub?.CreatedAt ?? DateTimeOffset.MinValue
        };
    }
}
