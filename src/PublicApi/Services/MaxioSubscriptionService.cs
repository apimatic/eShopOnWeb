using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _productFamilyHandle = settings.Value.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var products = await _client.Products.ListProducts(
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

            return products
                .Select(p => p.Product)
                .Where(p => p?.Handle != null)
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id ?? 0,
                    Name = p.Name ?? string.Empty,
                    Handle = p.Handle ?? string.Empty,
                    Description = p.Description ?? string.Empty,
                    PriceInCents = p.PriceInCents ?? 0,
                    Interval = p.Interval ?? 0,
                    IntervalUnit = p.IntervalUnit?.Value ?? "month",
                    ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty
                })
                .Where(p => p.ProductFamilyHandle == _productFamilyHandle)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to list subscription plans: HTTP {(int)ex.Error.StatusCode} - {ex.Error.ReadAsString()}", ex);
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string planHandle, CancellationToken ct = default)
    {
        var customer = await EnsureCustomerAsync(userId, ct);

        try
        {
            var subscriptionResponse = await _client.Subscriptions.CreateSubscription(
                body: new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerReference = userId,
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: ct);

            var sub = subscriptionResponse.Subscription;
            if (sub is null)
            {
                throw new InvalidOperationException("Subscription creation returned no subscription data.");
            }

            return MapSubscription(sub);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errBody))
            {
                var errors = string.Join("; ", errBody.Errors?.Select(e => e.ToString()) ?? Array.Empty<string>());
                throw new InvalidOperationException($"Subscription creation failed: {errors}", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw new InvalidOperationException(
                    $"Subscription creation failed: HTTP {(int)raw.StatusCode} - {raw.ReadAsString()}", ex);
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var customer = await EnsureCustomerAsync(userId, ct);
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customer.Id!.Value,
                ct: ct);

            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to list subscriptions: HTTP {(int)ex.Error.StatusCode} - {ex.Error.ReadAsString()}", ex);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);
            return existing.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return await CreateCustomerAsync(userId, ct);
        }
        catch (SdkException<RawError>)
        {
            return await CreateCustomerAsync(userId, ct);
        }
    }

    private async Task<Customer> CreateCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    {
                        FirstName = "eShop",
                        LastName = $"User-{userId}",
                        Email = $"user-{userId}@eshop.local",
                        Reference = userId
                    }
                },
                ct: ct);

            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errBody))
            {
                var existing = await _client.Customers.ReadCustomerByReference(
                    reference: userId,
                    ct: ct);
                return existing.Customer;
            }
            throw;
        }
    }

    private static SubscriptionDto MapSubscription(Subscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            State = sub.State?.Value ?? "unknown",
            PlanName = sub.Product?.Name ?? string.Empty,
            PlanHandle = sub.Product?.Handle ?? string.Empty,
            PriceInCents = sub.ProductPriceInCents ?? 0,
            NextBillingAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CreatedAt = sub.CreatedAt
        };
    }
}
