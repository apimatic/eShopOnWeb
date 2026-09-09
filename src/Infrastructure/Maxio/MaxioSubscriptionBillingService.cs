using System;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>. All Maxio
/// access goes through the generated SDK client; every provider, transport, timeout and parse
/// failure is translated to <see cref="SubscriptionBillingException"/> at the boundary so no SDK
/// detail leaks to callers. Registered as a singleton so the per-user subscribe lock is shared
/// across concurrent requests (the guard that makes a double-click idempotent within a process).
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Subscription states that are not "live" — a subscription in one of these does not block a
    // fresh subscribe and is not returned as the idempotent match.
    private static readonly HashSet<string> DeadStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _settings.ProductFamilyHandle!;
        // The productFamilyId path param takes a numeric id OR a handle prefixed with "handle:"
        // (per the operation's own docs). Handles are stable; ids are re-seeded — so use the handle.
        var familyId = $"handle:{familyHandle}";
        try
        {
            // Case A operation. All filter params are nullable-without-default, so pass them by name.
            var products = await BoundedAsync(
                ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
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
                    ct: ct),
                cancellationToken);

            return products
                .Select(pr => pr.Product)
                .Where(p => p is not null)
                .Select(MapPlan)
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                _logger.LogError(ex, "Maxio product family '{Family}' was not found while listing plans.", familyHandle);
                throw new SubscriptionBillingException(
                    "The configured subscription catalog is unavailable.", 502, 404, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
                throw TranslateRaw("list plans", raw, ex);
            throw new SubscriptionBillingException("Unexpected error listing subscription plans.", 502, null, ex);
        }
        catch (Exception ex) when (TryTranslateCommon("list plans", ex, cancellationToken, out var translated))
        {
            throw translated;
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        // Serialize a single user's subscribe calls so a double-click cannot race the
        // ensure-customer / guard-then-create steps below (process-local; see the plan file).
        var gate = _subscribeLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var customer = await EnsureCustomerAsync(subscriber, cancellationToken);
                if (customer.Id is not int customerId)
                    throw new SubscriptionBillingException("Billing customer is missing an identifier.", 502);

                // Guard: return an existing live subscription to this plan instead of creating a duplicate.
                var existing = await BoundedAsync(
                    ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                    cancellationToken);

                var live = existing
                    .Select(x => x.Subscription)
                    .FirstOrDefault(s => s is not null
                        && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                        && IsLive(s));

                if (live is not null)
                {
                    _logger.LogInformation(
                        "Reusing existing live subscription {SubscriptionId} for customer {CustomerId} on plan {Plan}.",
                        live.Id, customerId, planHandle);
                    return new SubscribeResult(MapSubscription(live), AlreadyExisted: true);
                }

                var body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customerId,
                        // Deterministic reference aids reconciliation of an ambiguous create.
                        Reference = $"eshop-{subscriber.Reference}-{planHandle}"
                    }
                };

                SubscriptionResponse created;
                try
                {
                    created = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(body, ct: ct), cancellationToken);
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    throw TranslateCreateSubscription(ex);
                }

                var subscription = created.Subscription
                    ?? throw new SubscriptionBillingException("The billing provider returned an empty subscription.", 502);

                _logger.LogInformation(
                    "Created subscription {SubscriptionId} for customer {CustomerId} on plan {Plan} (state {State}).",
                    subscription.Id, customerId, planHandle, subscription.State?.Value);

                return new SubscribeResult(MapSubscription(subscription), AlreadyExisted: false);
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRaw("subscribe", ex.Error, ex);
            }
            catch (Exception ex) when (TryTranslateCommon("subscribe", ex, cancellationToken, out var translated))
            {
                throw translated;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await FindCustomerAsync(subscriber.Reference, cancellationToken);
            if (customer?.Id is not int customerId)
                return Array.Empty<CustomerSubscription>();

            var subscriptions = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);

            return subscriptions
                .Select(x => x.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw("list subscriptions", ex.Error, ex);
        }
        catch (Exception ex) when (TryTranslateCommon("list subscriptions", ex, cancellationToken, out var translated))
        {
            throw translated;
        }
    }

    // ---- Maxio customer lifecycle -------------------------------------------------------------

    /// <summary>Looks a customer up by reference; returns null when none exists (404).</summary>
    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference: reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Ensures a Maxio customer exists for the user (idempotent by reference).</summary>
    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
            return existing;

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }
        };

        try
        {
            var created = await BoundedAsync(ct => _client.Customers.CreateCustomer(request, ct: ct), cancellationToken);
            var customer = created.Customer;
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", customer.Id, subscriber.Reference);
            return customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw TranslateCreateCustomer(ex);
        }
    }

    // ---- Timeout budget -----------------------------------------------------------------------

    /// <summary>
    /// Bounds a whole call with the configured budget, linked to the caller's token — the SDK's own
    /// <c>Timeout</c> is per attempt, so this is the only thing that caps a retried call end to end.
    /// </summary>
    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));
        return await call(cts.Token);
    }

    // ---- Error translation --------------------------------------------------------------------

    /// <summary>
    /// Handles the failure kinds every operation shares: a malformed 2xx body (<see cref="JsonException"/>),
    /// caller cancellation (rethrown), a timeout, and transport failure. Returns false for anything else
    /// (e.g. an operation-specific typed <see cref="SdkException{TError}"/>) so it propagates unchanged.
    /// </summary>
    private bool TryTranslateCommon(string operation, Exception ex, CancellationToken callerToken, out SubscriptionBillingException translated)
    {
        switch (ex)
        {
            case SubscriptionBillingException:
                translated = null!;
                return false; // already translated (e.g. from a nested create) — let it through
            case JsonException json:
                translated = TranslateParse(operation, json);
                return true;
            case OperationCanceledException when callerToken.IsCancellationRequested:
                translated = null!;
                return false; // genuine caller abort — do not convert, let it propagate
            case OperationCanceledException oce:
                _logger.LogWarning(oce, "Maxio {Operation} timed out after the request budget elapsed.", operation);
                translated = new SubscriptionBillingException("The billing provider did not respond in time.", 504, null, oce);
                return true;
            case HttpRequestException http:
                _logger.LogError(http, "Maxio {Operation} failed: provider unreachable.", operation);
                translated = new SubscriptionBillingException("The billing provider is unreachable.", 502, null, http);
                return true;
            default:
                translated = null!;
                return false;
        }
    }

    private SubscriptionBillingException TranslateRaw(string operation, RawError raw, Exception source)
    {
        var provider = (int)raw.StatusCode;
        _logger.LogError(source, "Maxio {Operation} failed: HTTP {Status}. Body: {Body}",
            operation, provider, Truncate(SafeReadBody(raw), 512));
        return new SubscriptionBillingException(FriendlyMessage(provider), SuggestStatus(provider), provider, source);
    }

    private SubscriptionBillingException TranslateParse(string operation, JsonException ex)
    {
        _logger.LogError(ex, "Maxio {Operation} returned a response that could not be processed.", operation);
        return new SubscriptionBillingException("The billing provider returned a response that could not be processed.", 502, null, ex);
    }

    private SubscriptionBillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var body))
        {
            var message = DescribeCustomerErrors(body);
            _logger.LogWarning(ex, "Maxio rejected customer creation (422): {Message}", message);
            return new SubscriptionBillingException(message, 422, 422, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
            return TranslateRaw("create customer", raw, ex);
        return new SubscriptionBillingException("The billing provider rejected the customer.", 502, null, ex);
    }

    private SubscriptionBillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var body))
        {
            var message = body.Errors is { Count: > 0 }
                ? string.Join("; ", body.Errors)
                : "The subscription request was rejected.";
            _logger.LogWarning(ex, "Maxio rejected subscription creation (422): {Message}", message);
            return new SubscriptionBillingException(message, 422, 422, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
            return TranslateRaw("create subscription", raw, ex);
        return new SubscriptionBillingException("The billing provider rejected the subscription.", 502, null, ex);
    }

    private static string DescribeCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is { } errors && errors.TryGetListOfString(out var list) && list.Count > 0)
            return string.Join("; ", list);
        return "The customer request was rejected.";
    }

    private static int SuggestStatus(int providerStatus) => providerStatus switch
    {
        401 or 403 => 502,  // our credentials — the caller did nothing wrong and cannot fix it
        429 => 503,         // our quota
        >= 400 and < 500 => providerStatus, // caller-actionable (422, 404, 409, ...)
        _ => 502,           // 5xx / unknown
    };

    private static string FriendlyMessage(int providerStatus) => providerStatus switch
    {
        401 or 403 => "The billing provider rejected our credentials.",
        429 => "The billing provider is rate limiting requests. Please retry shortly.",
        404 => "The requested billing resource was not found.",
        >= 400 and < 500 => "The billing request was rejected by the provider.",
        _ => "The billing provider is currently unavailable.",
    };

    private static string SafeReadBody(RawError raw)
    {
        try { return raw.ReadAsString(); }
        catch { return "<unreadable>"; }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";

    // ---- Mapping ------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product p) => new(
        Handle: p.Handle ?? string.Empty,
        Name: p.Name,
        Description: p.Description,
        PriceInCents: p.PriceInCents ?? 0,
        FormattedPrice: FormatPrice(p.PriceInCents),
        IntervalCount: p.Interval,
        IntervalUnit: p.IntervalUnit?.Value,
        RequiresPaymentMethod: p.RequireCreditCard ?? false);

    private static CustomerSubscription MapSubscription(Subscription s) => new(
        Id: s.Id ?? 0,
        PlanHandle: s.Product?.Handle,
        PlanName: s.Product?.Name,
        State: s.State?.Value,
        PriceInCents: s.ProductPriceInCents,
        FormattedPrice: FormatPrice(s.ProductPriceInCents),
        NextBillingDate: s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt: s.CurrentPeriodEndsAt,
        ActivatedAt: s.ActivatedAt,
        Reference: s.Reference);

    private static bool IsLive(Subscription s)
    {
        var state = s.State?.Value;
        return state is not null && !DeadStates.Contains(state);
    }

    private static string FormatPrice(long? cents) =>
        cents is null ? string.Empty : "$" + (cents.Value / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
