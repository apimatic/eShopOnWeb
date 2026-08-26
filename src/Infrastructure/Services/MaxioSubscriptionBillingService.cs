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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.DTOs;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing.
/// Identity mapping: the eShopOnWeb username is stored as the Maxio customer <c>reference</c>;
/// each subscription gets a deterministic reference <c>{username}:{productHandle}</c>, which makes
/// subscribe retry-safe (find-then-create, reconcile on raced/transport failures).
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    public const string HttpClientName = "Maxio";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FamilyIdCacheDuration = TimeSpan.FromHours(1);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var familyId = await GetProductFamilyIdAsync(cancellationToken);
            var products = await Bounded(
                t => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(),
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
                    ct: t),
                cancellationToken);

            return products
                .Select(p => p.Product)
                .Where(p => p is { Name: not null, Handle: not null })
                .Select(p => new SubscriptionPlanDto(
                    p.Id ?? 0,
                    p.Name!,
                    p.Handle!,
                    p.PriceInCents ?? 0,
                    p.Interval ?? 1,
                    p.IntervalUnit?.Value ?? IntervalUnit.Month.Value))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new BillingException("The configured product family was not found at the billing provider.", (int)HttpStatusCode.NotFound, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Rejected(raw, ex);
            }
            throw new BillingException("The billing provider rejected the request.", 502, ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Rejected(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<CustomerSubscriptionDto> SubscribeAsync(string username, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        var reference = $"{username}:{productHandle}";
        try
        {
            var existing = await TryFindSubscriptionAsync(reference, cancellationToken);
            if (existing?.Subscription is { } current)
            {
                return Map(current);
            }

            var customerId = await GetOrCreateCustomerIdAsync(username, email, cancellationToken);
            return await CreateSubscriptionAsync(customerId, productHandle, reference, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Rejected(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscriptionDto>> ListSubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        try
        {
            var customerId = await TryReadCustomerIdAsync(username, cancellationToken);
            if (customerId is not int id)
            {
                return Array.Empty<CustomerSubscriptionDto>();
            }

            var subscriptions = await Bounded(
                t => _client.Customers.ListCustomerSubscriptions(id, t),
                cancellationToken);

            return subscriptions
                .Where(s => s.Subscription is not null)
                .Select(s => Map(s.Subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw Rejected(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
    }

    private async Task<int> GetProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio:product-family-id:{_settings.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out int cachedId))
        {
            return cachedId;
        }

        var families = await Bounded(
            t => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: t),
            cancellationToken);

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f?.Handle == _settings.ProductFamilyHandle);

        if (match?.Id is not int familyId)
        {
            throw new BillingException(
                $"Product family '{_settings.ProductFamilyHandle}' was not found at the billing provider.",
                (int)HttpStatusCode.NotFound);
        }

        _cache.Set(cacheKey, familyId, FamilyIdCacheDuration);
        return familyId;
    }

    private async Task<int?> TryReadCustomerIdAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                t => _client.Customers.ReadCustomerByReference(username, t),
                cancellationToken);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<int> GetOrCreateCustomerIdAsync(string username, string email, CancellationToken cancellationToken)
    {
        var existingId = await TryReadCustomerIdAsync(username, cancellationToken);
        if (existingId is int foundId)
        {
            return foundId;
        }

        var (firstName, lastName) = DeriveNames(username);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = username
            }
        };

        try
        {
            var created = await Bounded(t => _client.Customers.CreateCustomer(body, t), cancellationToken);
            if (created.Customer.Id is int newId)
            {
                return newId;
            }
            throw new BillingException("The billing provider returned a customer without an id.", 502);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422 — possibly a concurrent create with the same reference; re-read before failing.
                var racedId = await TryReadCustomerIdAsync(username, cancellationToken);
                if (racedId is int id)
                {
                    return id;
                }
                throw new BillingException("The billing provider rejected the customer record (HTTP 422).", 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Rejected(raw, ex);
            }
            throw new BillingException("The billing provider rejected the customer record.", 502, ex);
        }
    }

    private async Task<SubscriptionResponse?> TryFindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(
                t => _client.Subscriptions.FindSubscription(reference, t),
                cancellationToken);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Rejected(raw, ex);
            }
            throw new BillingException("The billing provider rejected the request.", 502, ex);
        }
    }

    private async Task<CustomerSubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                // Invoice-based collection: the seeded plans capture no payment method, and the
                // site rejects automatic collection without one ("No payment method on file").
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await Bounded(t => _client.Subscriptions.CreateSubscription(body, t), cancellationToken);
            if (response.Subscription is { } subscription)
            {
                return Map(subscription);
            }
            throw new BillingException("The billing provider returned an empty subscription response.", 502);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                // 422 — possibly a raced double-submit with the same reference; reconcile first.
                var existing = await TryFindSubscriptionAsync(reference, cancellationToken);
                if (existing?.Subscription is { } raced)
                {
                    return Map(raced);
                }
                var messages = errorList.Errors is { Count: > 0 }
                    ? string.Join("; ", errorList.Errors)
                    : "The subscription was rejected (HTTP 422).";
                throw new BillingException(messages, 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Rejected(raw, ex);
            }
            throw new BillingException("The billing provider rejected the subscription request.", 502, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            // A transport failure on a write has an unknown outcome — the request may have reached
            // Maxio. Reconcile by the deterministic reference before reporting failure.
            _logger.LogWarning("Transport failure during subscription create; reconciling by reference.");
            var existing = await TryFindSubscriptionAsync(reference, CancellationToken.None);
            if (existing?.Subscription is { } settled)
            {
                return Map(settled);
            }
            throw new BillingException("The billing provider could not be reached and no subscription was recorded.", 503, ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static CustomerSubscriptionDto Map(Subscription subscription)
    {
        return new CustomerSubscriptionDto(
            subscription.Id ?? 0,
            subscription.State?.Value ?? "unknown",
            subscription.Product?.Name ?? string.Empty,
            subscription.Product?.Handle ?? string.Empty,
            subscription.ProductPriceInCents,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static (string FirstName, string LastName) DeriveNames(string username)
    {
        var local = username.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1
            ? (parts[0], string.Join(' ', parts.Skip(1)))
            : (local, "Customer");
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken)
    {
        return ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);
    }

    private static int StatusFor(RawError raw)
    {
        var status = (int)raw.StatusCode;
        return status is >= 400 and < 500 ? status : 502;
    }

    private BillingException Rejected(RawError raw, Exception ex)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio API error: HTTP {StatusCode}", status);
        return new BillingException($"The billing provider rejected the request (HTTP {status}).", StatusFor(raw), ex);
    }

    private BillingException Unprocessable(JsonException ex)
    {
        _logger.LogWarning("Maxio returned a response that could not be processed: {Message}", ex.Message);
        return new BillingException("The billing provider returned a response that could not be processed.", 502, ex);
    }

    private BillingException Unreachable(Exception ex)
    {
        _logger.LogWarning("Maxio could not be reached: {Message}", ex.Message);
        return new BillingException("The billing provider could not be reached.", 503, ex);
    }
}
