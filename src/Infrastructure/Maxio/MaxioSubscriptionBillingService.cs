using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing. Maxio is the system of
/// record for customers and subscriptions; the local <see cref="BuyerSubscription"/> row carries the
/// concurrency duplicate-claim and tracks the write for reconciliation. All provider/transport failures
/// are translated to <see cref="SubscriptionBillingException"/> so no SDK detail leaks to callers.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PlansPerPage = 100;
    private const int MaxPlanPages = 20;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IRepository<BuyerSubscription> _buyerSubscriptions;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IRepository<BuyerSubscription> buyerSubscriptions,
        MaxioSettings settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _buyerSubscriptions = buyerSubscriptions;
        _settings = settings;
        _logger = logger;
    }

    public Task<SubscriptionPlanList> GetPlansAsync(CancellationToken cancellationToken = default) =>
        ListPlansForFamilyAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException(BillingErrorKind.InvalidRequest, "A plan handle is required.");
        }

        // 1. The plan must be one of the configured family's plans (cross-operation invariant).
        var plans = await ListPlansForFamilyAsync(cancellationToken).ConfigureAwait(false);
        var plan = plans.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionBillingException(BillingErrorKind.PlanNotFound,
                $"Plan '{planHandle}' is not an available subscription plan.");
        }

        planHandle = plan.Handle; // normalise to the provider's exact handle casing
        var subscriptionReference = BuildSubscriptionReference(subscriber.UserId, planHandle);
        var spec = new BuyerSubscriptionByBuyerAndPlanSpecification(subscriber.UserId, planHandle);

        // 2. Ensure the Maxio customer exists (idempotent on the unique customer reference).
        var customerId = await EnsureCustomerAsync(subscriber, cancellationToken).ConfigureAwait(false);

        // 3. Idempotent fast-path: already provisioned → return the existing subscription, no new write.
        var existing = await _buyerSubscriptions.FirstOrDefaultAsync(spec, cancellationToken).ConfigureAwait(false);
        if (existing is { Status: BillingRecordStatus.Provisioned })
        {
            _logger.LogInformation(
                "Buyer {BuyerId} already subscribed to plan {PlanHandle}; returning existing subscription.",
                subscriber.UserId, planHandle);
            var current = await ReadExistingSubscriptionAsync(subscriptionReference, plan, cancellationToken).ConfigureAwait(false);
            return new SubscribeResult(current, customerId, subscriber.UserId, AlreadySubscribed: true);
        }

        // 4. WRITE ORDER: claim locally BEFORE the provider call. The unique index is the duplicate claim.
        BuyerSubscription claim;
        if (existing is null)
        {
            claim = new BuyerSubscription(subscriber.UserId, planHandle, subscriptionReference);
            claim.SetCustomer(customerId);
            try
            {
                await _buyerSubscriptions.AddAsync(claim, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // A concurrent subscribe won the race (SQL Server unique index). Reconcile and return it.
                _logger.LogInformation(
                    "Concurrent subscribe detected for buyer {BuyerId} plan {PlanHandle}; reconciling.",
                    subscriber.UserId, planHandle);
                var current = await ReadExistingSubscriptionAsync(subscriptionReference, plan, cancellationToken).ConfigureAwait(false);
                return new SubscribeResult(current, customerId, subscriber.UserId, AlreadySubscribed: true);
            }
        }
        else
        {
            // A prior Pending/Failed/Unknown row — reuse it for a fresh attempt.
            existing.BeginAttempt();
            existing.SetCustomer(customerId);
            await _buyerSubscriptions.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
            claim = existing;
        }

        // 5. Create the subscription in Maxio and settle the local row.
        return await CreateSubscriptionAsync(subscriber, plan, customerId, subscriptionReference, claim, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        var customer = await TryReadCustomerAsync(subscriber.UserId, cancellationToken).ConfigureAwait(false);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<SubscriptionSummary>(); // no customer yet → no subscriptions
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await Bounded(t => _client.Customers.ListCustomerSubscriptions(customerId, ct: t), cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider is currently unavailable.", ex);
        }

        return subscriptions.Select(r => MapSummary(r.Subscription, null)).ToList();
    }

    // ----- Plans -------------------------------------------------------------------------------------

    private async Task<SubscriptionPlanList> ListPlansForFamilyAsync(CancellationToken ct)
    {
        var familyId = await ResolveFamilyIdAsync(ct).ConfigureAwait(false);
        var familyIdText = familyId.ToString(CultureInfo.InvariantCulture);

        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await Bounded(t => _client.ProductFamilies.ListProductsForProductFamily(
                    familyIdText,
                    dateField: null, filter: null, startDate: null, endDate: null,
                    startDatetime: null, endDatetime: null, includeArchived: false, include: null,
                    page: page, perPage: PlansPerPage, ct: t), ct).ConfigureAwait(false);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                        "The configured subscription product family is not available.", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ToProviderException(raw, ex);
                }

                throw new SubscriptionBillingException(BillingErrorKind.Unknown,
                    "The billing provider returned an unrecognised error.", ex);
            }
            catch (JsonException ex)
            {
                throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                    "The billing provider returned a response that could not be processed.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                    "The billing provider is currently unavailable.", ex);
            }

            foreach (var pr in products)
            {
                var p = pr.Product;
                if (p?.Handle is null || p.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    Handle: p.Handle,
                    Name: p.Name ?? p.Handle,
                    Description: p.Description,
                    PriceInCents: p.PriceInCents ?? 0,
                    Interval: p.Interval,
                    IntervalUnit: p.IntervalUnit?.Value,
                    PaymentMethodRequired: p.RequireCreditCard ?? false));
            }

            if (products.Count < PlansPerPage)
            {
                break; // the provider signalled the last page
            }

            page++;
            if (page > MaxPlanPages)
            {
                truncated = true; // hard cap hit → the result is partial
                break;
            }
        }

        if (truncated)
        {
            _logger.LogWarning("Plan listing truncated at {MaxPages} pages for family {Handle}.",
                MaxPlanPages, _settings.ProductFamilyHandle);
        }

        return new SubscriptionPlanList(plans, truncated);
    }

    private async Task<int> ResolveFamilyIdAsync(CancellationToken ct)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await Bounded(t => _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: t), ct)
                .ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider is currently unavailable.", ex);
        }

        var match = families.FirstOrDefault(f =>
            string.Equals(f.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.ProductFamily?.Id is int id)
        {
            return id;
        }

        _logger.LogWarning("Configured Maxio product family handle {Handle} was not found on the site.",
            _settings.ProductFamilyHandle);
        throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
            "The configured subscription product family is not available.");
    }

    // ----- Customer ----------------------------------------------------------------------------------

    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var existing = await TryReadCustomerAsync(subscriber.UserId, ct).ConfigureAwait(false);
        if (existing?.Id is int existingId)
        {
            return existingId;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = Coalesce(subscriber.FirstName, "eShop"),
                LastName = Coalesce(subscriber.LastName, "Customer"),
                Email = Coalesce(subscriber.Email, subscriber.UserId + "@users.eshoponweb.invalid"),
                Reference = subscriber.UserId // unique per site → idempotent customer + duplicate claim
            }
        };

        try
        {
            var response = await Bounded(t => _client.Customers.CreateCustomer(body, ct: t), ct).ConfigureAwait(false);
            var id = response.Customer.Id
                ?? throw new SubscriptionBillingException(BillingErrorKind.Unknown, "The billing provider did not return a customer id.");
            _logger.LogInformation("Created Maxio customer {CustomerId} for buyer {BuyerId}.", id, subscriber.UserId);
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent create loses the race with a 422 (reference is unique). Re-read wins either way.
            var reread = await TryReadCustomerAsync(subscriber.UserId, ct).ConfigureAwait(false);
            if (reread?.Id is int rid)
            {
                return rid;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                _logger.LogWarning("Maxio rejected customer details for buyer {BuyerId}.", subscriber.UserId);
                throw new SubscriptionBillingException(BillingErrorKind.InvalidRequest,
                    "The billing provider rejected the customer details.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, ex);
            }

            throw new SubscriptionBillingException(BillingErrorKind.Unknown,
                "The billing provider returned an unrecognised error.", ex);
        }
        catch (JsonException ex)
        {
            var reread = await TryReadCustomerAsync(subscriber.UserId, ct).ConfigureAwait(false);
            if (reread?.Id is int rid)
            {
                return rid;
            }

            throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            // Unknown outcome: the create may have landed. The unique reference lets a re-read find it.
            var reread = await TryReadCustomerAsync(subscriber.UserId, ct).ConfigureAwait(false);
            if (reread?.Id is int rid)
            {
                return rid;
            }

            throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider was unreachable while creating the customer.", ex);
        }
    }

    /// <summary>Reads the Maxio customer for <paramref name="userId"/>, or null when none exists (404).</summary>
    private async Task<Customer?> TryReadCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(t => _client.Customers.ReadCustomerByReference(userId, ct: t), ct).ConfigureAwait(false);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider is currently unavailable.", ex);
        }
    }

    // ----- Subscription create + reconcile -----------------------------------------------------------

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        SubscriberIdentity subscriber, SubscriptionPlan plan, int customerId,
        string subscriptionReference, BuyerSubscription claim, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerReference = subscriber.UserId,
                Reference = subscriptionReference,
                // These plans require no payment method, so no card is ever sent. Remittance (invoice)
                // collection is used so Maxio issues an invoice instead of attempting an automatic charge
                // against a non-existent payment profile at signup (which the "automatic" default does).
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await Bounded(t => _client.Subscriptions.CreateSubscription(body, ct: t), ct).ConfigureAwait(false);
            var subscription = response.Subscription;
            var state = subscription?.State?.Value;

            if (string.Equals(state, "failed_to_create", StringComparison.Ordinal))
            {
                claim.MarkFailed();
                await _buyerSubscriptions.UpdateAsync(claim, ct).ConfigureAwait(false);
                _logger.LogWarning("Subscription for buyer {BuyerId} plan {PlanHandle} returned state failed_to_create.",
                    subscriber.UserId, plan.Handle);
                throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                    "The subscription could not be created by the billing provider.");
            }

            claim.MarkProvisioned(subscription?.Id, state);
            await _buyerSubscriptions.UpdateAsync(claim, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Subscribed buyer {BuyerId} to plan {PlanHandle}: subscription {SubscriptionId} state {State}.",
                subscriber.UserId, plan.Handle, subscription?.Id, state ?? "unknown");
            return new SubscribeResult(MapSummary(subscription, plan), customerId, subscriber.UserId, AlreadySubscribed: false);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            claim.MarkFailed();
            await _buyerSubscriptions.UpdateAsync(claim, ct).ConfigureAwait(false);

            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var detail = errors.Errors is { Count: > 0 }
                    ? string.Join("; ", errors.Errors)
                    : "The billing provider rejected the subscription.";
                _logger.LogWarning("Maxio rejected subscription for buyer {BuyerId} plan {PlanHandle}: {Detail}",
                    subscriber.UserId, plan.Handle, detail);
                throw new SubscriptionBillingException(BillingErrorKind.InvalidRequest, detail, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException(raw, ex);
            }

            throw new SubscriptionBillingException(BillingErrorKind.Unknown,
                "The billing provider returned an unrecognised error.", ex);
        }
        catch (JsonException ex)
        {
            // Ambiguous: a drifted 2xx OR an error body that didn't match its generated shape. The POST may
            // or may not have created the subscription → reconcile by re-reading, never guess.
            return await ReconcileAfterUnknownOutcomeAsync(subscriber, plan, customerId, subscriptionReference, claim, ex, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested(); // the caller cancelled → propagate, do not reconcile
            // Transport failure after the request may have been received → UNKNOWN OUTCOME → reconcile.
            return await ReconcileAfterUnknownOutcomeAsync(subscriber, plan, customerId, subscriptionReference, claim, ex, ct).ConfigureAwait(false);
        }
    }

    private async Task<SubscribeResult> ReconcileAfterUnknownOutcomeAsync(
        SubscriberIdentity subscriber, SubscriptionPlan plan, int customerId,
        string subscriptionReference, BuyerSubscription claim, Exception cause, CancellationToken ct)
    {
        _logger.LogWarning(cause,
            "Subscription create outcome unknown for buyer {BuyerId}; reconciling by reference {Reference}.",
            subscriber.UserId, subscriptionReference);

        var subscription = await FindSubscriptionByReferenceAsync(subscriptionReference, ct).ConfigureAwait(false);
        if (subscription is not null)
        {
            claim.MarkProvisioned(subscription.Id, subscription.State?.Value);
            await _buyerSubscriptions.UpdateAsync(claim, ct).ConfigureAwait(false);
            return new SubscribeResult(MapSummary(subscription, plan), customerId, subscriber.UserId, AlreadySubscribed: false);
        }

        claim.MarkOutcomeUnknown();
        await _buyerSubscriptions.UpdateAsync(claim, ct).ConfigureAwait(false);
        throw new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
            "The subscription request was sent but its outcome could not be confirmed. " +
            "Please review your subscriptions before retrying.", cause);
    }

    private async Task<SubscriptionSummary> ReadExistingSubscriptionAsync(
        string subscriptionReference, SubscriptionPlan plan, CancellationToken ct)
    {
        var subscription = await FindSubscriptionByReferenceAsync(subscriptionReference, ct).ConfigureAwait(false);
        if (subscription is not null)
        {
            return MapSummary(subscription, plan);
        }

        // Best-effort: we know the plan and that a subscription exists for this reference.
        return new SubscriptionSummary(null, subscriptionReference, plan.Handle, plan.Name, plan.PriceInCents, null, null, null);
    }

    /// <summary>Finds a subscription by our deterministic reference, or null when it cannot be confirmed.</summary>
    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(t => _client.Subscriptions.FindSubscription(reference, ct: t), ct).ConfigureAwait(false);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null; // 404 — not found
        }
        catch (SdkException<FindSubscriptionError>)
        {
            return null; // any other provider error during a reconciliation read → treat as unconfirmed
        }
        catch (JsonException)
        {
            return null; // unreadable → unconfirmed (caller marks the outcome unknown, never success)
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null; // could not reach the provider → unconfirmed
        }
    }

    // ----- Helpers -----------------------------------------------------------------------------------

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        // The SDK's Timeout is per attempt; a linked token with a total budget is the only thing that
        // bounds the whole (possibly retried) call, and it also stops work when the caller disconnects.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token).ConfigureAwait(false);
    }

    private SubscriptionBillingException ToProviderException(RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio returned HTTP {Status}: {Body}", status, SafeBody(raw));

        // Our credentials/quota, or a provider 5xx — the caller did nothing wrong and cannot fix it.
        if (status is 401 or 403 or 429 || status >= 500)
        {
            return new SubscriptionBillingException(BillingErrorKind.ProviderUnavailable,
                "The billing provider is currently unavailable.", inner);
        }

        if (status == 404)
        {
            return new SubscriptionBillingException(BillingErrorKind.PlanNotFound,
                "The requested billing resource was not found.", inner);
        }

        if (status >= 400)
        {
            return new SubscriptionBillingException(BillingErrorKind.InvalidRequest,
                "The billing provider rejected the request.", inner);
        }

        return new SubscriptionBillingException(BillingErrorKind.Unknown,
            "The billing provider returned an unexpected response.", inner);
    }

    private static string SafeBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static SubscriptionSummary MapSummary(Subscription? subscription, SubscriptionPlan? plan)
    {
        if (subscription is null)
        {
            return new SubscriptionSummary(null, null, plan?.Handle, plan?.Name, plan?.PriceInCents, null, null, null);
        }

        return new SubscriptionSummary(
            SubscriptionId: subscription.Id,
            Reference: subscription.Reference,
            PlanHandle: subscription.Product?.Handle ?? plan?.Handle,
            PlanName: subscription.Product?.Name ?? plan?.Name,
            PriceInCents: subscription.ProductPriceInCents ?? plan?.PriceInCents,
            State: subscription.State?.Value,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            NextBillingAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static string BuildSubscriptionReference(string userId, string planHandle) => $"eshop:{userId}:{planHandle}";

    private static string Coalesce(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
