using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="IMaxioBillingService"/>. Talks to Maxio
/// exclusively through the <c>AsadAli.AdvancedBilling.Sdk</c> client, and translates every SDK
/// failure (typed error, transport fault, unreadable body) into a <see cref="BillingException"/>
/// so no SDK type ever reaches the API surface.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // Product prices in this catalog are USD (the site's default settlement currency); the
    // Maxio product record does not carry a currency, so we surface the site default for display.
    private const string DefaultCurrency = "USD";
    private const string FamilyIdCacheKey = "maxio:product-family-id";

    // Subscription states that mean "no longer enrolled" — a subscription in one of these does
    // not block a fresh subscribe and is not treated as an idempotent hit.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly KeyedAsyncLock _subscribeLock;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        KeyedAsyncLock subscribeLock,
        IMemoryCache cache,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _subscribeLock = subscribeLock;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var products = await ListProductsAsync(cancellationToken).ConfigureAwait(false);
            return products
                .Select(p => p.Product)
                .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
                .Select(MapPlan)
                .ToList();
        }
        catch (BillingException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "list plans"); }
        catch (JsonException ex) { throw Malformed(ex, "list plans"); }
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingUser user, string planHandle, CancellationToken cancellationToken = default)
    {
        if (user is null) throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException("A plan handle is required to subscribe.", isClientError: true);

        // Serialize concurrent subscribe attempts for the same shopper so a double-click cannot
        // race past the find-or-create checks and create a duplicate customer/subscription.
        using var _ = await _subscribeLock.LockAsync(user.Reference, cancellationToken).ConfigureAwait(false);

        try
        {
            // Validate the requested plan is one of ours (a product in the configured family).
            var plans = await ListPlansAsync(cancellationToken).ConfigureAwait(false);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
                throw new BillingException($"Unknown plan '{planHandle}'. It is not offered in this catalog.", isClientError: true);

            // Ensure the Maxio customer exists (idempotent, keyed on the stable reference).
            var customer = await EnsureCustomerAsync(user, cancellationToken).ConfigureAwait(false);
            var customerId = RequireId(customer.Id, "customer");

            // Idempotency: if the shopper already holds a live subscription to this plan, return it.
            var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, user.Reference, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                _logger.LogInformation("Shopper {Reference} already subscribed to {Plan} (subscription {Id}); returning existing.",
                    user.Reference, plan.Handle, existing.SubscriptionId);
                return new SubscribeResult(existing, alreadyExisted: true);
            }

            var created = await CreateSubscriptionAsync(customerId, plan.Handle, user.Reference, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Subscribed shopper {Reference} to {Plan} (subscription {Id}, state {State}).",
                user.Reference, created.PlanHandle, created.SubscriptionId, created.State);
            return new SubscribeResult(created, alreadyExisted: false);
        }
        catch (BillingException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "subscribe"); }
        catch (JsonException ex) { throw Malformed(ex, "subscribe"); }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default)
    {
        if (user is null) throw new ArgumentNullException(nameof(user));

        try
        {
            var customer = await ReadCustomerOrNullAsync(user.Reference, cancellationToken).ConfigureAwait(false);
            if (customer?.Id is not int customerId)
                return Array.Empty<CustomerSubscription>(); // No Maxio customer yet → nothing to show.

            var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);
            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!, user.Reference))
                .ToList();
        }
        catch (BillingException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "list subscriptions"); }
        catch (JsonException ex) { throw Malformed(ex, "list subscriptions"); }
    }

    // ---- Maxio calls (each translates its own typed SDK error) -------------------------------

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(CancellationToken ct)
    {
        var familyId = await ResolveFamilyIdAsync(ct).ConfigureAwait(false);
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 200,
                ct: ct).ConfigureAwait(false);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateApi(ex.Error, "list products", ex);
        }
    }

    private async Task<int> ResolveFamilyIdAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(FamilyIdCacheKey, out int cached))
            return cached;

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "list product families");
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(pf => pf is not null && string.Equals(pf.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is not int id)
            throw new BillingException(
                $"The configured Maxio product family '{_settings.ProductFamilyHandle}' was not found on this site.",
                isClientError: false);

        _cache.Set(FamilyIdCacheKey, id, TimeSpan.FromMinutes(10));
        return id;
    }

    private async Task<Customer> EnsureCustomerAsync(BillingUser user, CancellationToken ct)
    {
        var existing = await ReadCustomerOrNullAsync(user.Reference, ct).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = user.Reference
            }
        };

        try
        {
            var created = await _client.Customers.CreateCustomer(body, ct).ConfigureAwait(false);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var status = HttpStatusCode.InternalServerError;
            var detail = string.Empty;
            if (ex.Error.TryGetRawError(out var raw))
            {
                status = raw.StatusCode;
                detail = SafeRead(raw);
            }

            // A duplicate reference (422/409) means a concurrent request already created the
            // customer — re-read by reference and use it, preserving idempotency.
            if (status is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
            {
                var raced = await ReadCustomerOrNullAsync(user.Reference, ct).ConfigureAwait(false);
                if (raced is not null)
                    return raced;

                throw new BillingException(
                    $"The billing customer could not be created{FormatDetail(detail)}.",
                    isClientError: true, ex);
            }

            throw TranslateStatus(status, detail, "create customer", ex);
        }
    }

    private async Task<Customer?> ReadCustomerOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct).ConfigureAwait(false);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // Unknown reference → no customer yet.
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "look up customer");
        }
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct).ConfigureAwait(false);
        var match = subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .FirstOrDefault(s =>
                string.Equals(s!.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                IsLive(s.State));

        return match is null ? null : MapSubscription(match, reference);
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "list customer subscriptions");
        }
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = planHandle,
                // Card-less signup: no payment attributes, and a non-automatic collection method
                // so Maxio does not demand a payment method for the recurring balance.
                PaymentCollectionMethod = ResolveCollectionMethod()
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct).ConfigureAwait(false);
            if (response.Subscription is null)
                throw new BillingException("Maxio accepted the subscription but returned no subscription details.", isClientError: false);

            return MapSubscription(response.Subscription, reference);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // Surface Maxio's own validation messages on a 422 (e.g. a site that still requires payment info).
            if (ex.Error.TryGetErrorListResponse1(out var list) && list?.Errors is { Count: > 0 } messages)
            {
                var joined = string.Join("; ", messages);
                _logger.LogWarning(ex, "Maxio rejected subscription for {Reference}: {Errors}", reference, joined);
                throw new BillingException($"The subscription was rejected: {joined}", isClientError: true, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
                throw TranslateStatus(raw.StatusCode, SafeRead(raw), "create subscription", ex);

            throw new BillingException("The subscription could not be created.", isClientError: false, ex);
        }
    }

    // ---- Mapping ------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product p) => new(
        handle: p.Handle ?? string.Empty,
        name: p.Name ?? string.Empty,
        productId: p.Id ?? 0,
        priceInCents: p.PriceInCents ?? 0,
        currency: DefaultCurrency,
        interval: p.Interval ?? 0,
        intervalUnit: p.IntervalUnit?.Value ?? string.Empty,
        description: p.Description);

    private static CustomerSubscription MapSubscription(Subscription s, string reference) => new(
        subscriptionId: s.Id ?? 0,
        planHandle: s.Product?.Handle ?? string.Empty,
        planName: s.Product?.Name ?? string.Empty,
        priceInCents: s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0,
        currency: DefaultCurrency,
        state: s.State?.Value ?? "unknown",
        // Maxio's Subscription has no next_billing_at; the end of the current period is when it bills next.
        currentPeriodEndsAt: s.CurrentPeriodEndsAt,
        nextBillingAt: s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        customerReference: reference);

    private static bool IsLive(SubscriptionState? state)
        => state?.Value is string v && !TerminalStates.Contains(v);

    private CollectionMethod? ResolveCollectionMethod()
    {
        var configured = _settings.PaymentCollectionMethod;
        return string.IsNullOrWhiteSpace(configured)
            ? null // Let Maxio apply its default (automatic — which requires a payment method).
            : CollectionMethod.FromValue(configured.Trim().ToLowerInvariant());
    }

    // ---- Error translation --------------------------------------------------------------------

    private static int RequireId(int? id, string what)
        => id ?? throw new BillingException($"Maxio returned a {what} without an id.", isClientError: false);

    private BillingException TranslateApi(ApiError error, string op, Exception ex)
    {
        if (error.TryGetRawError(out var raw))
            return TranslateStatus(raw.StatusCode, SafeRead(raw), op, ex);
        return TranslateStatus(HttpStatusCode.InternalServerError, string.Empty, op, ex);
    }

    private BillingException TranslateRaw(SdkException<RawError> ex, string op)
        => TranslateStatus(ex.Error.StatusCode, SafeRead(ex.Error), op, ex);

    private BillingException TranslateStatus(HttpStatusCode status, string body, string op, Exception ex)
    {
        var client = IsClientStatus(status);
        if (client)
            _logger.LogWarning(ex, "Maxio {Op} rejected: {Status} {Body}", op, (int)status, body);
        else
            _logger.LogError(ex, "Maxio {Op} failed: {Status} {Body}", op, (int)status, body);

        var message = client
            ? $"The billing request was rejected ({(int)status}){FormatDetail(body)}."
            : "The billing provider is currently unavailable. Please try again later.";
        return new BillingException(message, client, ex);
    }

    private BillingException Unreachable(Exception ex, string op)
    {
        _logger.LogError(ex, "Maxio {Op}: provider unreachable", op);
        return new BillingException("The billing provider is currently unreachable. Please try again later.", isClientError: false, ex);
    }

    private BillingException Malformed(Exception ex, string op)
    {
        _logger.LogError(ex, "Maxio {Op}: response could not be processed", op);
        return new BillingException("The billing provider returned a response that could not be processed.", isClientError: false, ex);
    }

    private static bool IsClientStatus(HttpStatusCode status) => status is
        HttpStatusCode.BadRequest or
        HttpStatusCode.NotFound or
        HttpStatusCode.Conflict or
        HttpStatusCode.UnprocessableEntity;

    private static string SafeRead(RawError error)
    {
        try { return error.ReadAsString() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string FormatDetail(string detail)
        => string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail.Trim()}";
}
