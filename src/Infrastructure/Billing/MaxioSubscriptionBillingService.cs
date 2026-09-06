using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing.
/// <para>
/// Registered as a singleton: it holds only the (singleton) SDK client, immutable settings, and the
/// per-shopper subscribe gates below.
/// </para>
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Ceiling on one public operation — not one HTTP attempt. The SDK's per-attempt timeout and the
    /// HttpClient timeout each bound a single socket; only a token bounds what the caller experiences,
    /// and it lives here so every operation is covered by construction rather than per call site.
    /// </summary>
    internal static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Handles are stable, numeric ids are not — Maxio reassigns them when a catalog is re-seeded.
    /// The resolved id is therefore cached only briefly, and a 404 against it forces a fresh lookup.
    /// </summary>
    private static readonly TimeSpan FamilyIdCacheTtl = TimeSpan.FromMinutes(5);

    private const int PlansPageSize = 50;
    private const int MaxPlanPages = 20;

    /// <summary>
    /// The states in which Maxio no longer considers an enrollment live. Everything else — including
    /// <c>past_due</c>, <c>on_hold</c> and <c>trial_ended</c> — still represents a subscription the
    /// shopper has, so signing them up again would bill them twice.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Canceled.Value,
        SubscriptionState.Expired.Value,
        SubscriptionState.FailedToCreate.Value
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    /// <summary>
    /// Serializes concurrent subscribe attempts for one shopper, so a double-click cannot race its own
    /// find-or-create. Keyed by customer reference and therefore bounded by the number of shoppers who
    /// have ever subscribed on this host; entries are cheap and are intentionally not evicted, since a
    /// gate removed while another request holds it would silently stop serializing.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeGates = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _familyLookupGate = new(1, 1);

    /// <summary>
    /// Cached family id and its expiry as one immutable value, swapped by reference. Two separate fields
    /// would be read outside the lock without being written atomically together.
    /// </summary>
    private sealed record FamilyCacheEntry(int Id, DateTimeOffset ExpiresAt)
    {
        public bool IsFresh => ExpiresAt > DateTimeOffset.UtcNow;
    }

    private FamilyCacheEntry? _familyCache;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    private string ProductFamilyHandle => _settings.ProductFamilyHandle ?? string.Empty;

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        RunAsync("listing subscription plans", LoadPlansAsync, cancellationToken);

    private async Task<IReadOnlyList<SubscriptionPlan>> LoadPlansAsync(CancellationToken ct)
    {
        var familyId = await ResolveProductFamilyIdAsync(forceRefresh: false, ct).ConfigureAwait(false);

        try
        {
            return await ListPlansAsync(familyId, ct).ConfigureAwait(false);
        }
        catch (BillingProviderException ex) when (ex.ProviderStatusCode == 404)
        {
            // The cached family id no longer exists — the catalog was re-seeded under us.
            // Re-resolve from the (stable) handle and try once more.
            _logger.LogInformation(
                "Maxio product family id {FamilyId} is stale; re-resolving handle {Handle}.",
                familyId, ProductFamilyHandle);

            familyId = await ResolveProductFamilyIdAsync(forceRefresh: true, ct).ConfigureAwait(false);
            return await ListPlansAsync(familyId, ct).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<CustomerSubscription>>("listing your subscriptions", async ct =>
        {
            var reference = MaxioCustomerReference.ForEmail(subscriber.Email);

            var customer = await FindCustomerByReferenceAsync(reference, ct).ConfigureAwait(false);
            if (customer?.Id is not int customerId)
            {
                // Never enrolled: an empty list is the honest answer, not an error.
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct).ConfigureAwait(false);
            return subscriptions.Select(MapSubscription).ToList();
        }, cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default) =>
        RunAsync("creating your subscription", async ct =>
        {
            var reference = MaxioCustomerReference.ForEmail(subscriber.Email);
            var gate = _subscribeGates.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));

            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await SubscribeCoreAsync(subscriber, reference, planHandle, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }, cancellationToken);

    private async Task<SubscribeResult> SubscribeCoreAsync(
        SubscriberIdentity subscriber, string reference, string planHandle, CancellationToken ct)
    {
        // Resolve the plan before anything is created. This scopes signups to the plans the shopper was
        // actually offered — an archived plan, or one from another product family, is not purchasable —
        // and it supplies the billing interval the first billing date is derived from.
        var plan = (await LoadPlansAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new BillingProviderException(
                $"'{planHandle}' is not a plan you can subscribe to. See GET /api/subscription-plans for the current list.",
                BillingFailureKind.ProviderRejected,
                404);
        }

        var customer = await EnsureCustomerAsync(subscriber, reference, ct).ConfigureAwait(false);
        if (customer.Id is not int customerId)
        {
            throw new BillingProviderException(
                "The billing provider returned a customer record without an identifier.",
                BillingFailureKind.ProviderUnavailable);
        }

        var existing = await ListCustomerSubscriptionsAsync(customerId, ct).ConfigureAwait(false);

        // Double-click / retry: already enrolled in this exact plan — hand back what they have.
        var samePlan = existing.Select(MapSubscription)
            .FirstOrDefault(s => s.IsLive && string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));
        if (samePlan is not null)
        {
            _logger.LogInformation(
                "Maxio customer {Reference} is already subscribed to {PlanHandle} (subscription {SubscriptionId}); returning it.",
                reference, planHandle, samePlan.Id);
            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, samePlan);
        }

        // Enrolled in something else: refuse rather than quietly double-bill. Switching plans is a
        // separate capability and is deliberately out of scope here.
        var otherPlan = existing.Select(MapSubscription).FirstOrDefault(s => s.IsLive);
        if (otherPlan is not null)
        {
            throw new SubscriptionConflictException(
                $"You already have an active subscription to '{otherPlan.PlanHandle}'. Cancel it before subscribing to '{planHandle}'.",
                otherPlan.PlanHandle,
                otherPlan.Id);
        }

        var created = await CreateSubscriptionAsync(customerId, plan, ct).ConfigureAwait(false);
        return new SubscribeResult(SubscribeOutcome.Created, created);
    }

    // ---------------------------------------------------------------------------------------------
    // Product family / plans
    // ---------------------------------------------------------------------------------------------

    private async Task<int> ResolveProductFamilyIdAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && Volatile.Read(ref _familyCache) is { IsFresh: true } cached)
        {
            return cached.Id;
        }

        await _familyLookupGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && Volatile.Read(ref _familyCache) is { IsFresh: true } stillFresh)
            {
                return stillFresh.Id;
            }

            IReadOnlyList<ProductFamilyResponse> families;
            try
            {
                // No "read product family by handle" operation exists — the read takes a numeric id —
                // so the stable handle is matched against the list client-side.
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
                throw TranslateRawError(ex.Error, "looking up the product family", ex);
            }
            catch (JsonException ex)
            {
                throw TranslateUnreadable("looking up the product family", ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw TranslateTransport("looking up the product family", ex);
            }

            var match = families
                .Select(response => response.ProductFamily)
                .FirstOrDefault(family => family is not null
                    && string.Equals(family.Handle, ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

            if (match?.Id is not int familyId)
            {
                throw new BillingProviderException(
                    $"No product family with handle '{ProductFamilyHandle}' exists on the configured Maxio site.",
                    BillingFailureKind.NotConfigured);
            }

            Volatile.Write(ref _familyCache, new FamilyCacheEntry(familyId, DateTimeOffset.UtcNow.Add(FamilyIdCacheTtl)));
            return familyId;
        }
        finally
        {
            _familyLookupGate.Release();
        }
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(int familyId, CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var familyIdText = familyId.ToString(CultureInfo.InvariantCulture);

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyIdText,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PlansPageSize,
                    ct: ct).ConfigureAwait(false);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    // The typed accessor firing IS the status: this operation maps 404 to a string body.
                    throw new BillingProviderException(
                        $"The configured Maxio product family could not be found.",
                        BillingFailureKind.ProviderRejected,
                        404,
                        ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRawError(raw, "listing subscription plans", ex);
                }

                throw TranslateUnreadable("listing subscription plans", ex);
            }
            catch (JsonException ex)
            {
                // This operation deserializes its 404 body as a bare JSON string; if the provider answers
                // with an object the parse fails and takes the SdkException (and the status) with it.
                // An unreadable answer is never a successful, empty catalog.
                throw TranslateUnreadable("listing subscription plans", ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw TranslateTransport("listing subscription plans", ex);
            }

            plans.AddRange(pageItems
                .Select(item => item.Product)
                // ArchivedAt is the only availability signal the product model carries, and
                // includeArchived: false is not something the model lets us re-verify — so filter here too.
                .Where(product => product is not null && product.ArchivedAt is null)
                .Select(MapPlan));

            if (pageItems.Count < PlansPageSize)
            {
                break;
            }
        }

        return plans;
    }

    // ---------------------------------------------------------------------------------------------
    // Customers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Find-or-create, keyed on the derived reference. Maxio permits at most one customer per reference
    /// value, which is what makes a concurrent double-submit safe: the loser of the race re-reads and
    /// uses the winner's customer instead of creating a second one.
    /// </summary>
    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, string reference, CancellationToken ct)
    {
        var existing = await FindCustomerByReferenceAsync(reference, ct).ConfigureAwait(false);
        if (existing?.Id is not null)
        {
            return existing;
        }

        var (firstName, lastName) = MaxioCustomerReference.NamesForEmail(subscriber.Email);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = reference
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body: body, ct: ct).ConfigureAwait(false);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.",
                response.Customer.Id, reference);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here is most often "that reference is taken" — i.e. we lost a race with our own
            // concurrent request. The generated 422 shape cannot be trusted to say so (it models
            // `errors` as an object of pagination fields), so the recovery is never keyed on its text.
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                return await ResolveCustomerAfterFailedCreateAsync(reference, ex, ct).ConfigureAwait(false);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "creating your billing customer", ex);
            }

            throw TranslateUnreadable("creating your billing customer", ex);
        }
        catch (JsonException ex)
        {
            // Either a 2xx body we could not read (the customer may well exist) or an error body that
            // did not match its generated shape (which destroyed the status). Both are settled the same
            // way: ask the provider what is actually there.
            return await ResolveCustomerAfterFailedCreateAsync(reference, ex, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            // The create may have reached the provider before the connection died — re-read rather than
            // assume nothing happened.
            return await ResolveCustomerAfterFailedCreateAsync(reference, ex, ct).ConfigureAwait(false);
        }
    }

    private async Task<Customer> ResolveCustomerAfterFailedCreateAsync(string reference, Exception cause, CancellationToken ct)
    {
        var existing = await FindCustomerByReferenceAsync(reference, ct).ConfigureAwait(false);
        if (existing?.Id is not null)
        {
            _logger.LogInformation(
                "Maxio customer for reference {Reference} already existed after a failed create; reusing it.",
                reference);
            return existing;
        }

        _logger.LogWarning(cause, "Creating the Maxio customer for reference {Reference} failed and no customer exists.", reference);
        throw new BillingProviderException(
            "The billing provider rejected the request to create your billing customer.",
            BillingFailureKind.ProviderRejected,
            null,
            cause);
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct)
                .ConfigureAwait(false);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Genuinely absent — matched on the provider's own "not found", never on an unreadable body.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "looking up your billing customer", ex);
        }
        catch (JsonException ex)
        {
            // "I could not read the answer" is not "there is no customer": mapping it to an absence here
            // would turn a corrupt response into a duplicate customer.
            throw TranslateUnreadable("looking up your billing customer", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw TranslateTransport("looking up your billing customer", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Subscriptions
    // ---------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            // The customer-scoped list carries no state filter and no paging — filtering is ours to do.
            var responses = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct)
                .ConfigureAwait(false);

            return responses
                .Select(response => response.Subscription)
                .Where(subscription => subscription is not null)
                .Select(subscription => subscription!)
                .ToList();
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Array.Empty<Subscription>();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "listing your subscriptions", ex);
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadable("listing your subscriptions", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw TranslateTransport("listing your subscriptions", ex);
        }
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId, SubscriptionPlan plan, CancellationToken ct)
    {
        var planHandle = plan.Handle;
        var firstBillingAt = FirstBillingDate(plan, DateTimeOffset.UtcNow);

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                // Identify the existing customer by id — never together with customer_reference, and
                // never with customer_attributes, which would create a second customer.
                CustomerId = customerId,
                // Without this the provider assesses and charges the first period at signup, and refuses
                // the whole create when there is no payment profile — which is every signup here, since
                // these plans do not require a card. A future timestamp captures no payment at creation
                // and schedules the first capture then, i.e. the shopper is billed in arrears at the end
                // of their first period rather than getting it free.
                NextBillingAt = firstBillingAt
            }
        };

        try
        {
            var response = await SendCreateSubscriptionAsync(body, ct).ConfigureAwait(false);

            if (response.Subscription is not { } subscription)
            {
                // 2xx with nothing usable in it: the enrollment probably exists. Go and look.
                return await ReconcileCreatedSubscriptionAsync(customerId, planHandle, null, ct).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} to {PlanHandle} for customer {CustomerId}.",
                subscription.Id, planHandle, customerId);

            return MapSubscription(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var detail = errorList.Errors is { Count: > 0 }
                    ? string.Join("; ", errorList.Errors)
                    : "the request was rejected";
                _logger.LogWarning(
                    "Maxio rejected the subscription to {PlanHandle} for customer {CustomerId}: {Detail}",
                    planHandle, customerId, detail);

                throw new BillingProviderException(
                    $"The billing provider rejected this subscription: {detail}",
                    BillingFailureKind.ProviderRejected,
                    422,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "creating your subscription", ex);
            }

            throw TranslateUnreadable("creating your subscription", ex);
        }
        catch (JsonException ex)
        {
            // Could be an unreadable 2xx (the subscription exists) or an error body that did not match
            // its generated shape (it does not). Only the provider knows — ask it.
            return await ReconcileCreatedSubscriptionAsync(customerId, planHandle, ex, ct).ConfigureAwait(false);
        }
        catch (UnconfirmedWriteException ex) when (ex.InnerException is OperationCanceledException)
        {
            // The POST left the building and we ran out of budget before hearing back. There is no time
            // left to reconcile, and "failed" would be a guess — say only what we actually know.
            _logger.LogWarning(
                ex.InnerException,
                "Timed out awaiting the Maxio subscription to {PlanHandle} for customer {CustomerId} after the request was sent.",
                planHandle, customerId);

            throw new BillingProviderException(
                "We could not confirm whether your subscription was created before the request timed out. Please check your subscriptions before trying again.",
                BillingFailureKind.OutcomeUnknown,
                null,
                ex.InnerException);
        }
        catch (UnconfirmedWriteException ex)
        {
            return await ReconcileCreatedSubscriptionAsync(customerId, planHandle, ex.InnerException, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            // Nothing was ever sent (a send that did go out arrives as UnconfirmedWriteException), so this
            // one really is a plain failure.
            throw TranslateTransport("creating your subscription", ex);
        }
    }

    /// <summary>
    /// Puts the create on the wire under an "at most one send" scope, and nothing else.
    /// <para>
    /// The retry pipeline resends on a transport failure for every verb, and a reset thrown after the
    /// provider received the bytes looks identical to one thrown before — so a blocked resend, not a
    /// hopeful retry, is what keeps enrollments unique. The scope is released the moment the send is
    /// over: leaving it open across the reconcile that follows would have the guard refuse the very read
    /// that settles the outcome.
    /// </para>
    /// </summary>
    private async Task<SubscriptionResponse> SendCreateSubscriptionAsync(CreateSubscriptionRequest body, CancellationToken ct)
    {
        using var singleSend = new SingleSendScope();

        try
        {
            return await _client.Subscriptions.CreateSubscription(body: body, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (singleSend.HasSent
                                   && (IsBlockedResend(ex) || IsTransportFailure(ex) || ex is OperationCanceledException))
        {
            // A request that failed on the way out may still have been received. "This may already have
            // taken effect" is the only safe reading.
            throw new UnconfirmedWriteException(ex);
        }
    }

    /// <summary>
    /// Settles an unknown write outcome by re-reading provider state. Only the provider can say whether
    /// the enrollment exists, and answering "failed" when it does would leave the shopper billed for a
    /// subscription we told them they do not have.
    /// </summary>
    private async Task<CustomerSubscription> ReconcileCreatedSubscriptionAsync(
        int customerId, string planHandle, Exception? cause, CancellationToken ct)
    {
        _logger.LogWarning(
            cause,
            "The outcome of creating a Maxio subscription to {PlanHandle} for customer {CustomerId} is unknown; reconciling.",
            planHandle, customerId);

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct).ConfigureAwait(false);
        var match = subscriptions
            .Select(MapSubscription)
            .FirstOrDefault(s => s.IsLive && string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            _logger.LogInformation(
                "Reconciled: Maxio subscription {SubscriptionId} to {PlanHandle} does exist for customer {CustomerId}.",
                match.Id, planHandle, customerId);
            return match;
        }

        throw new BillingProviderException(
            "We could not confirm whether your subscription was created. Please check your subscriptions before trying again.",
            BillingFailureKind.OutcomeUnknown,
            null,
            cause);
    }

    /// <summary>
    /// When the provider should first capture payment. One billing period ahead, so the schedule the
    /// shopper is shown matches the plan they bought.
    /// </summary>
    internal static DateTimeOffset FirstBillingDate(SubscriptionPlan plan, DateTimeOffset now)
    {
        var interval = plan.Interval > 0 ? plan.Interval : 1;

        if (string.Equals(plan.IntervalUnit, IntervalUnit.Day.Value, StringComparison.OrdinalIgnoreCase))
        {
            return now.AddDays(interval);
        }

        // The provider models only day and month intervals; month is the safe reading of anything else,
        // and the value only has to be in the future for the charge to be deferred rather than captured.
        return now.AddMonths(interval);
    }

    // ---------------------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------------------

    private SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0L,
        Currency = _settings.Currency,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
        // require_credit_card is the hard requirement; request_credit_card only asks a signup form to
        // collect one, and is true on plans that subscribe perfectly well without a card. Reporting the
        // latter as "payment method required" would tell shoppers a card is needed when it is not.
        RequiresPaymentMethod = product.RequireCreditCard ?? false
    };

    private CustomerSubscription MapSubscription(Subscription subscription)
    {
        var state = subscription.State?.Value;
        var product = subscription.Product;

        return new CustomerSubscription
        {
            Id = subscription.Id ?? 0,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? product?.Handle ?? string.Empty,
            State = state ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0L,
            Currency = string.IsNullOrWhiteSpace(subscription.Currency) ? _settings.Currency : subscription.Currency!,
            Interval = product?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit?.Value ?? string.Empty,
            // The provider does not echo next_billing_at. current_period_ends_at is when the next
            // regularly scheduled charge occurs; next_assessment_at usually tracks it but diverges to a
            // dunning retry time after a failed renewal — so reading that one first would show a retry
            // time as the "next billing date" precisely for the subscriptions people go and look at.
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            BalanceInCents = subscription.BalanceInCents ?? 0L,
            // An unrecognised state is treated as live: over-reporting an enrollment is safe,
            // under-reporting it would let a second subscription through.
            IsLive = state is null || !TerminalStates.Contains(state)
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Failure translation — one ladder, applied identically at every call site
    // ---------------------------------------------------------------------------------------------

    private async Task<T> RunAsync<T>(string what, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(OperationBudget);

        try
        {
            return await operation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException(
                $"The billing provider did not respond within {OperationBudget.TotalSeconds:0} seconds while {what}.",
                BillingFailureKind.Timeout,
                null,
                ex);
        }
    }

    private BillingProviderException TranslateRawError(RawError raw, string what, Exception cause)
    {
        var status = (int)raw.StatusCode;

        // 401/403 mean our credentials or site are wrong, 429 means we are being throttled. None of
        // those are the caller's fault, so they must not be reflected back as a client error.
        var kind = status is >= 400 and < 500 && status is not (401 or 403 or 429)
            ? BillingFailureKind.ProviderRejected
            : BillingFailureKind.ProviderUnavailable;

        _logger.LogWarning(
            cause,
            "Maxio returned HTTP {StatusCode} while {What}: {Body}",
            status, what, ReadBodySafely(raw));

        var message = kind == BillingFailureKind.ProviderRejected
            ? $"The billing provider rejected the request while {what}."
            : $"The billing provider is currently unavailable ({what}).";

        return new BillingProviderException(message, kind, status, cause);
    }

    private BillingProviderException TranslateUnreadable(string what, Exception cause)
    {
        _logger.LogWarning(cause, "Maxio returned a response that could not be processed while {What}.", what);
        return new BillingProviderException(
            $"The billing provider returned a response that could not be processed while {what}.",
            BillingFailureKind.ProviderUnavailable,
            null,
            cause);
    }

    private BillingProviderException TranslateTransport(string what, Exception cause)
    {
        _logger.LogWarning(cause, "Maxio could not be reached while {What}.", what);
        return new BillingProviderException(
            $"The billing provider could not be reached while {what}.",
            BillingFailureKind.ProviderUnavailable,
            null,
            cause);
    }

    private static string ReadBodySafely(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return body.Length > 512 ? body.Substring(0, 512) + "..." : body;
        }
        catch (Exception)
        {
            return "<unreadable>";
        }
    }

    /// <summary>Connection-level failures, which no <c>SdkException</c> catch will ever match.</summary>
    private static bool IsTransportFailure(Exception ex) => ex is HttpRequestException;

    /// <summary>Our own guard refusing a resend — it can arrive wrapped, so walk the chain.</summary>
    private static bool IsBlockedResend(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is DuplicateSendBlockedException)
            {
                return true;
            }
        }

        return false;
    }
}
