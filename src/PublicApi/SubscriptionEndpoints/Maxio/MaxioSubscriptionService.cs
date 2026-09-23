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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

/// <summary>
/// Subscription-billing service backed by Maxio Advanced Billing. Maxio is the system of record:
/// there is no local subscription store. Idempotency and reconciliation are keyed on a deterministic
/// <c>reference</c> derived from the eShop user id, so every write can be looked up again without any
/// local state.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // A single deadline for the whole operation. Per-attempt SDK/HttpClient timeouts do NOT bound a
    // multi-call operation (subscribe makes up to ~5 calls), so this linked-token budget is what the
    // caller actually waits at most.
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(60);

    // Backstop for the plans page loop — the family cannot realistically exceed this many products.
    private const int MaxPlanPages = 25;
    private const int PlansPerPage = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    // ---- Deterministic references (the idempotency / reconciliation keys) ----

    private static string CustomerReference(string eShopUserId) => $"eshop-user:{eShopUserId}";

    private static string SubscriptionReference(string eShopUserId, string planHandle) =>
        $"eshop-sub:{eShopUserId}:{planHandle}";

    // =====================================================================================
    // GET PLANS
    // =====================================================================================

    public async Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken callerCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
        cts.CancelAfter(OperationBudget);
        var ct = cts.Token;

        var familyId = "handle:" + _settings.ProductFamilyHandle;
        var plans = new List<MaxioPlan>();

        try
        {
            var page = 1;
            for (; page <= MaxPlanPages; page++)
            {
                // Named args: many leading optional params have no C# default and must be passed as null.
                IReadOnlyList<ProductResponse> pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PlansPerPage,
                    ct: ct);

                foreach (var pr in pageItems)
                {
                    var p = pr.Product;
                    if (p?.Handle is null || p.Name is null)
                        continue; // a plan we cannot address by handle is not subscribable — skip it
                    plans.Add(new MaxioPlan(
                        Handle: p.Handle,
                        Name: p.Name,
                        Description: p.Description,
                        PriceInCents: p.PriceInCents ?? 0,
                        Interval: p.Interval,
                        IntervalUnit: p.IntervalUnit?.Value));
                }

                if (pageItems.Count < PlansPerPage)
                    break; // last page — the list is complete by construction
            }

            if (page > MaxPlanPages)
                _logger.LogWarning("Maxio plan listing hit the {MaxPlanPages}-page cap for family {Family}",
                    MaxPlanPages, _settings.ProductFamilyHandle);

            return plans;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            // Case A. 404 => the configured product family handle does not exist on the site.
            if (ex.Error.TryGetString(out var notFound))
                throw new MaxioIntegrationException(
                    $"Configured Maxio product family '{_settings.ProductFamilyHandle}' was not found.",
                    HttpStatusCode.NotFound, isCallerError: false, inner: ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw Provider(raw, ex);
            throw new MaxioIntegrationException("The billing provider returned an unrecognised error.", inner: ex);
        }
        catch (Exception ex) when (IsTransportOrTimeout(ex, callerCt))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    // =====================================================================================
    // SUBSCRIBE  (the hero flow)
    // =====================================================================================

    public async Task<SubscribeResult> SubscribeAsync(MaxioUserContext user, string planHandle, CancellationToken callerCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
        cts.CancelAfter(OperationBudget);
        var ct = cts.Token;

        // CROSS-OPERATION INVARIANT: the plan handle must be one the plans listing returns for the
        // configured family (a value accepted by CreateSubscription must be one ListProducts... returns).
        var plans = await GetPlansAsync(ct);
        if (plans.All(p => !string.Equals(p.Handle, planHandle, StringComparison.Ordinal)))
            throw new MaxioIntegrationException(
                $"Unknown plan '{planHandle}'. Choose one of the available subscription plans.",
                HttpStatusCode.BadRequest, isCallerError: true);

        // Ensure the customer exists (idempotent).
        var customerId = await EnsureCustomerAsync(user, ct, callerCt);

        // REPEATED OPERATIONS gate: if a live subscription to this plan already exists, return it and
        // do NOT create a second one.
        var existing = (await ListCustomerSubscriptionsAsync(customerId, ct))
            .FirstOrDefault(s => string.Equals(s.Subscription?.Product?.Handle, planHandle, StringComparison.Ordinal)
                                 && IsLive(s.Subscription?.State));
        if (existing?.Subscription is not null)
        {
            _logger.LogInformation(
                "User {UserId} already has a live subscription ({SubscriptionId}) to plan {Plan}; returning it",
                user.EShopUserId, existing.Subscription.Id, planHandle);
            return new SubscribeResult(ToView(existing.Subscription), AlreadySubscribed: true);
        }

        var subRef = SubscriptionReference(user.EShopUserId, planHandle);
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = subRef,
                // Collect by invoice rather than automatic card capture: these plans require no payment
                // method, so enrolling must not attempt to auto-charge the first period (which fails with
                // "no payment method on file"). Remittance is the Relationship-Invoicing collection method.
                PaymentCollectionMethod = CollectionMethod.Remittance
                // product_price_point_handle omitted -> the product's default price point applies.
            }
        };

        _logger.LogInformation("Subscribing user {UserId} to plan {Plan} (customer {CustomerId}, ref {Ref})",
            user.EShopUserId, planHandle, customerId, subRef);

        Subscription created;
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body: body, ct: ct);
            created = response.Subscription
                      ?? throw new MaxioIntegrationException("The billing provider returned an empty subscription.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // Case A. A 422 here can be a duplicate-reference rejection (a concurrent double-submit that
            // beat us past the existence check) OR a genuine validation error. Reconcile by reference to
            // tell them apart: if the subscription now exists, the duplicate write is the winner.
            var reconciled = await TryFindSubscriptionAsync(subRef, ct);
            if (reconciled is not null)
            {
                _logger.LogInformation("Subscription {Ref} already existed (concurrent create); returning it", subRef);
                return new SubscribeResult(ToView(reconciled), AlreadySubscribed: true);
            }

            if (ex.Error.TryGetErrorListResponse1(out var errors))
                throw new MaxioIntegrationException(
                    "Subscription was rejected: " + string.Join("; ", errors.Errors),
                    HttpStatusCode.UnprocessableEntity, isCallerError: true, inner: ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw Provider(raw, ex);
            throw new MaxioIntegrationException("The billing provider rejected the subscription.", inner: ex);
        }
        catch (Exception ex) when (IsTransportOrTimeout(ex, callerCt))
        {
            // UNKNOWN OUTCOME: the create may have reached Maxio before the failure. Re-read by reference
            // rather than reporting a definite failure.
            _logger.LogWarning(ex, "CreateSubscription for {Ref} failed in transit; reconciling by re-read", subRef);
            var reconciled = await TryFindSubscriptionAsync(subRef, ct);
            if (reconciled is not null)
                return new SubscribeResult(ToView(reconciled), AlreadySubscribed: true);
            throw new MaxioIntegrationException(
                "The subscription could not be confirmed with the billing provider. Please retry.", inner: ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }

        var view = ToView(created);
        _logger.LogInformation("Subscribed user {UserId}: subscription {SubscriptionId} state {State}",
            user.EShopUserId, view.Id, view.State);
        return new SubscribeResult(view, AlreadySubscribed: false);
    }

    // =====================================================================================
    // MY SUBSCRIPTIONS
    // =====================================================================================

    public async Task<IReadOnlyList<MaxioSubscriptionView>> GetMySubscriptionsAsync(MaxioUserContext user, CancellationToken callerCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
        cts.CancelAfter(OperationBudget);
        var ct = cts.Token;

        // A read must not create a customer. If the user has never subscribed, they have no Maxio
        // customer yet -> empty list.
        var customer = await ReadCustomerByReferenceOrNullAsync(CustomerReference(user.EShopUserId), ct, callerCt);
        if (customer?.Id is not int customerId)
            return Array.Empty<MaxioSubscriptionView>();

        var subs = await ListCustomerSubscriptionsAsync(customerId, ct);
        return subs
            .Where(s => s.Subscription is not null)
            .Select(s => ToView(s.Subscription!))
            .ToList();
    }

    // =====================================================================================
    // Customer (idempotent ensure)
    // =====================================================================================

    private async Task<int> EnsureCustomerAsync(MaxioUserContext user, CancellationToken ct, CancellationToken callerCt)
    {
        var reference = CustomerReference(user.EShopUserId);

        // Fast path: already exists.
        var existing = await ReadCustomerByReferenceOrNullAsync(reference, ct, callerCt);
        if (existing?.Id is int existingId)
            return existingId;

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = reference // the idempotency claim: Maxio enforces per-site reference uniqueness
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body: body, ct: ct);
            var id = response.Customer?.Id
                     ?? throw new MaxioIntegrationException("The billing provider returned a customer without an id.");
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", id, user.EShopUserId);
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Case A. A duplicate reference (concurrent create) rejects here; re-read the winner.
            var reconciled = await ReadCustomerByReferenceOrNullAsync(reference, ct, callerCt);
            if (reconciled?.Id is int reconciledId)
                return reconciledId;

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
                throw new MaxioIntegrationException(
                    "The billing provider rejected the customer details.",
                    HttpStatusCode.UnprocessableEntity, isCallerError: true, inner: ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw Provider(raw, ex);
            throw new MaxioIntegrationException("The billing provider rejected creating the customer.", inner: ex);
        }
        catch (Exception ex) when (IsTransportOrTimeout(ex, callerCt))
        {
            // UNKNOWN OUTCOME: the create may have landed. Reconcile by reference.
            _logger.LogWarning(ex, "CreateCustomer for {Ref} failed in transit; reconciling by re-read", reference);
            var reconciled = await ReadCustomerByReferenceOrNullAsync(reference, ct, callerCt);
            if (reconciled?.Id is int reconciledId)
                return reconciledId;
            throw new MaxioIntegrationException(
                "The customer could not be confirmed with the billing provider. Please retry.", inner: ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceOrNullAsync(string reference, CancellationToken ct, CancellationToken callerCt)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // Case B: no customer with this reference yet
        }
        catch (SdkException<RawError> ex)
        {
            throw Provider(ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportOrTimeout(ex, callerCt))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    // =====================================================================================
    // Subscription reads
    // =====================================================================================

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw Provider(ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    /// <summary>Best-effort reconciliation read; returns null on not-found or any failure.</summary>
    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError>)
        {
            return null; // 404 (TryGetNoContent) or any other error -> treat as "not found" for reconciliation
        }
        catch (Exception)
        {
            return null; // reconciliation is best-effort; the caller surfaces the unknown outcome
        }
    }

    // =====================================================================================
    // Mapping / classification
    // =====================================================================================

    private static bool IsLive(SubscriptionState? state)
    {
        var v = state?.Value;
        // Anything that is not a terminal end-of-life state counts as an active enrollment.
        return v is not (null or "canceled" or "expired" or "failed_to_create");
    }

    private static SubscriptionOutcome Classify(SubscriptionState? state)
    {
        return state?.Value switch
        {
            "active" or "trialing" => SubscriptionOutcome.Active,
            // not-yet: includes an absent/unreadable state — never coalesced to success
            "pending" or "assessing" or "awaiting_signup" or null => SubscriptionOutcome.Provisioning,
            _ => SubscriptionOutcome.AttentionRequired
        };
    }

    private static MaxioSubscriptionView ToView(Subscription s) => new(
        Id: s.Id ?? 0,
        Reference: s.Reference,
        State: s.State?.Value ?? "unknown",
        Outcome: Classify(s.State),
        ProductHandle: s.Product?.Handle,
        ProductName: s.Product?.Name,
        PriceInCents: s.ProductPriceInCents ?? s.Product?.PriceInCents,
        CurrentPeriodEndsAt: s.CurrentPeriodEndsAt,
        NextBillingAt: s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);

    // =====================================================================================
    // Failure translation helpers
    // =====================================================================================

    private static bool IsTransportOrTimeout(Exception ex, CancellationToken callerCt) =>
        (ex is HttpRequestException || ex is OperationCanceledException) && !callerCt.IsCancellationRequested;

    private MaxioIntegrationException Provider(RawError raw, Exception inner)
    {
        var status = raw.StatusCode;
        // Provider 4xx the caller can act on -> caller error; auth/quota and 5xx are ours.
        var isCaller = (int)status is >= 400 and < 500 and not 401 and not 403 and not 429;
        string detail;
        try { detail = raw.ReadAsString(); }
        catch { detail = string.Empty; }
        _logger.LogError(inner, "Maxio provider error {Status}: {Detail}", (int)status, detail);
        return new MaxioIntegrationException(
            $"The billing provider returned HTTP {(int)status}.", status, isCaller, inner);
    }

    private MaxioIntegrationException Unreachable(Exception inner)
    {
        _logger.LogError(inner, "Maxio provider unreachable");
        return new MaxioIntegrationException("The billing provider is currently unreachable.", inner: inner);
    }

    private MaxioIntegrationException Unprocessable(Exception inner)
    {
        _logger.LogError(inner, "Maxio returned a response that could not be processed");
        return new MaxioIntegrationException(
            "The billing provider returned a response that could not be processed.", inner: inner);
    }
}
