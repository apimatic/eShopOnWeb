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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>. Maps the SDK's models and
/// exceptions onto the provider-agnostic domain contract, keeps the eShop-user ↔ Maxio-customer link in Maxio
/// (customer <c>reference</c> = the eShop user id), and serializes per-user writes to stay idempotent under
/// double-submit. Registered as a singleton (holds the per-user lock table alongside the long-lived SDK client).
/// </summary>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    // Overall per-call budget. The SDK's Timeout is per-attempt; only a CancellationToken deadline bounds a
    // whole call (retries + backoff). Enforced by a linked CTS around every SDK call.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(100);

    // Subscription states that are "done" — a subscription in one of these does not block re-subscribing.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    // One gate per eShop user, so a double-clicked subscribe cannot race itself into two customers/subscriptions
    // within this process (the host is single-process; the customer-reference dedup covers cross-process).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    // The products-for-family endpoint takes the numeric family id, not the handle, so we resolve the
    // configured handle to its id once and cache it for the process lifetime (re-seed → restart to refresh).
    private readonly SemaphoreSlim _familyIdLock = new(1, 1);
    private int? _cachedFamilyId;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var productFamilyId = await ResolveProductFamilyIdAsync(cancellationToken);

        using var cts = LinkedBudget(cancellationToken);
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: productFamilyId,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 200,
                ct: cts.Token);

            return products
                .Select(r => r.Product)
                .Where(p => p is not null && !string.IsNullOrEmpty(p.Handle))
                .Select(MapPlan)
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                // 404 — the configured product family handle does not exist on the site (a misconfiguration).
                throw new SubscriptionBillingException(
                    $"The configured product family '{_settings.ProductFamilyHandle}' was not found in the billing provider.",
                    SubscriptionBillingErrorKind.ProviderUnavailable, 404, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }

            throw Unrecognized(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructure(ex);
        }
    }

    public async Task<SubscribeOutcome> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validate the plan against the configured family up front — gives a clean 4xx for an unknown handle
        // and gives us the product id/handle to match existing subscriptions against.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionBillingException(
                $"Unknown plan '{request.PlanHandle}'. Choose one of the available subscription plans.",
                SubscriptionBillingErrorKind.InvalidRequest, 400);
        }

        var gate = _userLocks.GetOrAdd(request.UserReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerId = await EnsureCustomerAsync(request, cancellationToken);

            // Idempotency: reuse an existing live subscription to the same plan instead of creating a duplicate.
            var existing = await ListSubscriptionsRawAsync(customerId, cancellationToken);
            var live = existing.FirstOrDefault(s => !IsTerminal(s.State) && MatchesPlan(s, plan));
            if (live is not null)
            {
                _logger.LogInformation(
                    "Reusing existing {State} subscription {SubscriptionId} for user {UserReference} on plan {PlanHandle}",
                    live.State?.Value, live.Id, request.UserReference, plan.Handle);
                return new SubscribeOutcome(MapSubscription(live), WasCreated: false);
            }

            var created = await CreateSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            _logger.LogInformation(
                "Created subscription {SubscriptionId} for user {UserReference} on plan {PlanHandle}",
                created.Id, request.UserReference, plan.Handle);
            return new SubscribeOutcome(created, WasCreated: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerAsync(userReference, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var raw = await ListSubscriptionsRawAsync(customerId, cancellationToken);
        return raw.Select(MapSubscription).ToList();
    }

    // --- product family resolution (handle -> numeric id, cached) --------------------------------------

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_cachedFamilyId is int cached)
        {
            return cached.ToString(CultureInfo.InvariantCulture);
        }

        await _familyIdLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedFamilyId is int already)
            {
                return already.ToString(CultureInfo.InvariantCulture);
            }

            IReadOnlyList<ProductFamilyResponse> families;
            using (var cts = LinkedBudget(cancellationToken))
            {
                try
                {
                    families = await _client.ProductFamilies.ListProductFamilies(
                        dateField: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        ct: cts.Token);
                }
                catch (SdkException<RawError> ex)
                {
                    throw Translate(ex.Error, ex);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsInfrastructureFailure(ex))
                {
                    throw TranslateInfrastructure(ex);
                }
            }

            var match = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => f is not null && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

            if (match?.Id is not int id)
            {
                throw new SubscriptionBillingException(
                    $"The configured product family '{_settings.ProductFamilyHandle}' was not found in the billing provider.",
                    SubscriptionBillingErrorKind.ProviderUnavailable, 404);
            }

            _cachedFamilyId = id;
            return id.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _familyIdLock.Release();
        }
    }

    // --- customer ensure (idempotent by reference) ------------------------------------------------------

    private async Task<int> EnsureCustomerAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(request.UserReference, cancellationToken);
        if (existing is not null)
        {
            return existing.Id ?? throw new SubscriptionBillingException(
                "The billing provider returned a customer without an id.", SubscriptionBillingErrorKind.Unexpected);
        }

        using var cts = LinkedBudget(cancellationToken);
        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = request.Email,
                    Reference = request.UserReference
                }
            };

            var response = await _client.Customers.CreateCustomer(body, ct: cts.Token);
            var id = response.Customer.Id
                     ?? throw new SubscriptionBillingException(
                         "The billing provider returned a customer without an id.", SubscriptionBillingErrorKind.Unexpected);

            _logger.LogInformation("Created billing customer {CustomerId} for user {UserReference}", id, request.UserReference);
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here may be a create race (another request created the customer with the same reference
            // between our read and create). Reconcile by re-reading by reference before treating it as an error.
            var raced = await FindCustomerAsync(request.UserReference, cancellationToken);
            if (raced?.Id is int racedId)
            {
                _logger.LogInformation("Customer for user {UserReference} already existed (create race); reusing {CustomerId}", request.UserReference, racedId);
                return racedId;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new SubscriptionBillingException(
                    "The billing provider rejected the customer details.", SubscriptionBillingErrorKind.InvalidRequest, 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }

            throw Unrecognized(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructure(ex);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var cts = LinkedBudget(cancellationToken);
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cts.Token);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // No customer with this reference yet. This is an absence, matched on the 404 status only —
            // never on a parse failure, which is a different fact (see catch below).
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructure(ex);
        }
    }

    // --- subscriptions ----------------------------------------------------------------------------------

    private async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        using var cts = LinkedBudget(cancellationToken);
        try
        {
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    // Product handle + customer id is enough to enroll. The collection method (default remittance)
                    // makes the balance an invoice rather than an immediate card charge, so no payment method is required.
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = CollectionMethod.FromValue(_settings.PaymentCollectionMethod)
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body, ct: cts.Token);
            if (response.Subscription is null)
            {
                throw new SubscriptionBillingException(
                    "The billing provider returned an empty subscription.", SubscriptionBillingErrorKind.Unexpected);
            }

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var detail = errors.Errors is { Count: > 0 } ? string.Join("; ", errors.Errors) : "validation failed";
                throw new SubscriptionBillingException(
                    $"The billing provider rejected the subscription: {detail}", SubscriptionBillingErrorKind.InvalidRequest, 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }

            throw Unrecognized(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructure(ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListSubscriptionsRawAsync(int customerId, CancellationToken cancellationToken)
    {
        using var cts = LinkedBudget(cancellationToken);
        try
        {
            var list = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cts.Token);
            return list
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructure(ex);
        }
    }

    // --- mapping ----------------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product p) => new()
    {
        Handle = p.Handle!,
        Name = string.IsNullOrEmpty(p.Name) ? p.Handle! : p.Name!,
        Description = p.Description,
        PriceInCents = p.PriceInCents ?? 0,
        IntervalCount = p.Interval,
        IntervalUnit = p.IntervalUnit?.Value,
        ProductId = p.Id
    };

    private static CustomerSubscription MapSubscription(Subscription s) => new()
    {
        Id = s.Id ?? 0,
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        PriceInCents = s.ProductPriceInCents ?? s.Product?.PriceInCents,
        State = s.State?.Value ?? "unknown",
        NextBillingAt = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CreatedAt = s.CreatedAt
    };

    private static bool MatchesPlan(Subscription s, SubscriptionPlan plan) =>
        (s.Product?.Handle is string handle && string.Equals(handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
        || (plan.ProductId is int productId && s.Product?.Id == productId);

    private static bool IsTerminal(SubscriptionState? state) =>
        state is not null && TerminalStates.Contains(state.Value);

    // --- error translation ------------------------------------------------------------------------------

    private CancellationTokenSource LinkedBudget(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private static bool IsInfrastructureFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;

    private static SubscriptionBillingException Translate(RawError raw, Exception source)
    {
        var status = (int)raw.StatusCode;
        return status switch
        {
            // Our credentials or our quota — the caller did nothing wrong and cannot fix it.
            401 or 403 => new SubscriptionBillingException(
                "The billing provider rejected our credentials.", SubscriptionBillingErrorKind.ProviderUnavailable, status, source),
            429 => new SubscriptionBillingException(
                "The billing provider is rate-limiting requests. Please try again shortly.", SubscriptionBillingErrorKind.ProviderUnavailable, status, source),
            // The provider rejected the caller's request — surface it as a caller-actionable 4xx.
            >= 400 and < 500 => new SubscriptionBillingException(
                $"The billing provider rejected the request (HTTP {status}).", SubscriptionBillingErrorKind.InvalidRequest, status, source),
            // Provider 5xx — unavailable.
            _ => new SubscriptionBillingException(
                "The billing provider is currently unavailable.", SubscriptionBillingErrorKind.ProviderUnavailable, status, source)
        };
    }

    private SubscriptionBillingException TranslateInfrastructure(Exception ex)
    {
        if (ex is JsonException)
        {
            // A 2xx (or unrecognized error) body that could not be deserialized — outcome could not be read.
            _logger.LogError(ex, "The billing provider returned a response that could not be processed.");
            return new SubscriptionBillingException(
                "The billing provider returned a response that could not be processed.", SubscriptionBillingErrorKind.ProviderUnavailable, null, ex);
        }

        // Transport failure or the per-call budget elapsing (TaskCanceledException not tied to the caller token).
        _logger.LogError(ex, "The billing provider was unreachable or timed out.");
        return new SubscriptionBillingException(
            "The billing provider is currently unavailable.", SubscriptionBillingErrorKind.ProviderUnavailable, null, ex);
    }

    private SubscriptionBillingException Unrecognized(Exception ex)
    {
        _logger.LogError(ex, "The billing provider returned an unrecognized error shape.");
        return new SubscriptionBillingException(
            "The billing provider returned an unrecognized error.", SubscriptionBillingErrorKind.Unexpected, null, ex);
    }
}
