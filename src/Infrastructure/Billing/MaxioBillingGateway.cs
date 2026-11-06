using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="IBillingGateway"/>. All SDK failures are translated
/// here into <see cref="BillingException"/> with caller-safe messages.
/// </summary>
public class MaxioBillingGateway : IBillingGateway
{
    private const int PageSize = 100;
    private const int MaxPages = 50;

    private readonly Lazy<MaxioAdvancedBillingClient> _client;
    private readonly string? _familyHandle;

    public MaxioBillingGateway(Lazy<MaxioAdvancedBillingClient> client, string? productFamilyHandle)
    {
        _client = client;
        _familyHandle = productFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_familyHandle))
            throw new BillingException(BillingFailureKind.Unavailable, "Billing is not configured (Maxio:ProductFamilyHandle).");

        var plans = new List<SubscriptionPlan>();
        try
        {
            var client = _client.Value;
            for (var page = 1; page <= MaxPages; page++)
            {
                var items = await client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + _familyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PageSize,
                    ct: ct);

                foreach (var item in items)
                {
                    var p = item.Product;
                    if (p.Handle == null || p.ArchivedAt != null)
                        continue;
                    plans.Add(new SubscriptionPlan(
                        p.Handle,
                        p.Name ?? p.Handle,
                        p.Description,
                        (p.PriceInCents ?? 0) / 100m,
                        p.Interval ?? 1,
                        p.IntervalUnit?.Value ?? "month"));
                }

                if (items.Count < PageSize)
                    break;
            }
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
                throw new BillingException(BillingFailureKind.NotFound, "The product family was not found.", ex);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw FromStatus(raw.StatusCode, ex);
            throw new BillingException(BillingFailureKind.ProviderError, "The billing system returned an error.", ex);
        }
        catch (Exception ex) when (IsTranslatable(ex, ct))
        {
            throw Translate(ex);
        }

        return plans;
    }

    public async Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Value.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsTranslatable(ex, ct))
        {
            throw Translate(ex);
        }
    }

    public async Task<int> CreateCustomerAsync(BillingCustomer customer, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Value.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email,
                    Reference = customer.Reference
                }
            }, ct);
            return response.Customer.Id
                ?? throw new BillingException(BillingFailureKind.ProviderError, "The billing system returned an unexpected response.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The generated 422 model may not match the live body, so fall back to the raw status.
            if (ex.Error.TryGetRawError(out RawError raw))
                throw FromStatus(raw.StatusCode, ex, "The billing system rejected the customer details.");
            throw new BillingException(BillingFailureKind.Validation, "The billing system rejected the customer details.", ex);
        }
        catch (Exception ex) when (IsTranslatable(ex, ct))
        {
            throw Translate(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Value.Customers.ListCustomerSubscriptions(customerId, ct);
            return response
                .Where(r => r.Subscription != null)
                .Select(r => Map(r.Subscription!))
                .ToList();
        }
        catch (Exception ex) when (IsTranslatable(ex, ct))
        {
            throw Translate(ex);
        }
    }

    public async Task<SubscriptionInfo> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Value.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    // No card capture in this flow: bill by invoice (remittance) instead of auto-charging.
                    PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                }
            }, ct);
            var subscription = response.Subscription
                ?? throw new BillingException(BillingFailureKind.ProviderError, "The billing system returned an unexpected response.");
            return Map(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out ErrorListResponse1 errors) && errors.Errors.Count > 0)
                throw new BillingException(BillingFailureKind.Validation,
                    "The subscription could not be created: " + string.Join("; ", errors.Errors), ex);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw FromStatus(raw.StatusCode, ex, "The subscription could not be created.");
            throw new BillingException(BillingFailureKind.ProviderError, "The billing system returned an error.", ex);
        }
        catch (Exception ex) when (IsTranslatable(ex, ct))
        {
            throw Translate(ex);
        }
    }

    private static SubscriptionInfo Map(Subscription s) => new(
        s.Id?.ToString() ?? string.Empty,
        s.State?.Value ?? "unknown",
        s.Product?.Handle ?? string.Empty,
        s.Product?.Name ?? string.Empty,
        (s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0) / 100m,
        s.Product?.Interval ?? 1,
        s.Product?.IntervalUnit?.Value ?? "month",
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);

    private static bool IsTranslatable(Exception ex, CancellationToken ct)
        => ex is not BillingException && !(ex is OperationCanceledException && ct.IsCancellationRequested);

    /// <summary>Shared tail of every catch ladder: raw SDK errors, transport failures, unreadable bodies.</summary>
    private static BillingException Translate(Exception ex) => ex switch
    {
        SdkException<RawError> sdk => FromStatus(sdk.Error.StatusCode, ex),
        HttpRequestException or TaskCanceledException =>
            new BillingException(BillingFailureKind.Unavailable, "The billing system could not be reached.", ex),
        JsonException =>
            new BillingException(BillingFailureKind.ProviderError, "The billing system returned a response that could not be processed.", ex),
        _ => new BillingException(BillingFailureKind.ProviderError, "The billing system returned an error.", ex)
    };

    private static BillingException FromStatus(HttpStatusCode status, Exception inner, string? validationMessage = null)
    {
        var code = (int)status;
        return code switch
        {
            404 => new BillingException(BillingFailureKind.NotFound, "The requested billing resource was not found.", inner),
            400 or 409 or 422 => new BillingException(BillingFailureKind.Validation,
                validationMessage ?? "The billing system rejected the request.", inner),
            _ => new BillingException(BillingFailureKind.ProviderError,
                $"The billing system returned an error (HTTP {code}).", inner)
        };
    }
}
