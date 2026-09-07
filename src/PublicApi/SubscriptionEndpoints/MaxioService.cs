using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly Dictionary<int, int> _userSubscriptionMap = new();

    public MaxioService(MaxioAdvancedBillingClient client, string productFamilyHandle)
    {
        _client = client;
        _productFamilyHandle = productFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        var productFamily = await GetProductFamilyByHandleAsync(_productFamilyHandle, ct);
        if (productFamily == null)
            return Array.Empty<SubscriptionPlan>();

        var products = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: productFamily.Id.ToString(),
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

        return products
            .Where(p => p.Product != null)
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Product.Handle ?? string.Empty,
                Name = p.Product.Name ?? string.Empty,
                Price = p.Product.PriceInCents.HasValue ? (decimal)p.Product.PriceInCents.Value / 100 : 0,
                Interval = p.Product.Interval ?? 1,
                IntervalUnit = p.Product.IntervalUnit?.Value ?? "month"
            })
            .ToList();
    }

    public async Task<SubscriptionInfo?> CreateOrGetSubscriptionAsync(
        int userId,
        string userEmail,
        string userFirstName,
        string userLastName,
        string productHandle,
        CancellationToken ct = default)
    {
        var customer = await GetOrCreateCustomerAsync(userId, userEmail, userFirstName, userLastName, ct);
        if (customer?.Id == null)
            return null;

        var subscriptionRef = $"{userId}-{productHandle}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";

        try
        {
            var createSubRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = productHandle,
                    Reference = subscriptionRef
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: createSubRequest, ct: ct);

            if (response?.Subscription != null)
            {
                _userSubscriptionMap[userId] = response.Subscription.Id ?? 0;

                return new SubscriptionInfo
                {
                    SubscriptionId = response.Subscription.Id ?? 0,
                    State = response.Subscription.State?.Value ?? "unknown",
                    CurrentPeriodEndsAt = response.Subscription.CurrentPeriodEndsAt,
                    ProductHandle = productHandle
                };
            }
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new InvalidOperationException($"Failed to create subscription: subscription creation failed", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                var errorText = rawError.ReadAsString();
                throw new InvalidOperationException($"Failed to create subscription: {errorText}", ex);
            }
            throw;
        }

        return null;
    }

    public async Task<SubscriptionInfo?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(
                subscriptionId: subscriptionId,
                include: null,
                ct: ct);

            if (response?.Subscription != null)
            {
                return new SubscriptionInfo
                {
                    SubscriptionId = response.Subscription.Id ?? 0,
                    State = response.Subscription.State?.Value ?? "unknown",
                    CurrentPeriodEndsAt = response.Subscription.CurrentPeriodEndsAt
                };
            }
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            var errorText = ex.Error.ReadAsString();
            throw new InvalidOperationException($"Failed to read subscription: {errorText}", ex);
        }

        return null;
    }

    public IReadOnlyList<SubscriptionInfo> GetUserSubscriptions(int userId)
    {
        if (!_userSubscriptionMap.TryGetValue(userId, out var subscriptionId))
            return Array.Empty<SubscriptionInfo>();

        return new[] { new SubscriptionInfo { SubscriptionId = subscriptionId } };
    }

    private async Task<Customer?> GetOrCreateCustomerAsync(
        int userId,
        string email,
        string firstName,
        string lastName,
        CancellationToken ct = default)
    {
        var reference = userId.ToString();

        try
        {
            var existingCustomer = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return existingCustomer?.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode != System.Net.HttpStatusCode.NotFound)
                throw;
        }

        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            };

            var response = await _client.Customers.CreateCustomer(body: createRequest, ct: ct);
            return response?.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var customerError))
            {
                throw new InvalidOperationException($"Failed to create customer: customer creation failed", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                var errorText = rawError.ReadAsString();
                throw new InvalidOperationException($"Failed to create customer: {errorText}", ex);
            }
            throw;
        }
    }

    private async Task<ProductFamily?> GetProductFamilyByHandleAsync(string handle, CancellationToken ct = default)
    {
        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct);

            return families?.FirstOrDefault(f => f.ProductFamily?.Handle == handle)?.ProductFamily;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to retrieve product families: {ex.Message}", ex);
        }
    }
}
