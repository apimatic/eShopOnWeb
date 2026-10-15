using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscription billing with Maxio Advanced Billing as the system of record.
/// <para>
/// Subscribe order: validate the plan against the offered catalog → claim the shopper's
/// <see cref="SubscriptionEnrollment"/> row (the store refuses a second claim) → find or create the Maxio
/// customer by its deterministic reference → adopt a live subscription if Maxio already has one, else create
/// it with a per-attempt subscription reference → record the result. A create whose outcome is unknown is
/// settled by looking it up by its reference.
/// </para>
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    // Whole-call budgets: what the caller of each endpoint waits at most for Maxio, retries included.
    internal static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(30);
    // Budget for re-reading Maxio after a write failed ambiguously; independent of the caller's token.
    internal static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);
    // A pending enrollment older than this belongs to a request that has finished (or died) without recording it.
    internal static readonly TimeSpan StalePendingAfter = TimeSpan.FromMinutes(2);

    internal const int PlansPerPage = 200;
    internal const int MaxPlanPages = 5;
    private const int MaxClaimAttempts = 3;

    private static readonly SubscriptionState[] EndedStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ISubscriptionEnrollmentStore _store;
    private readonly MaxioSiteProfileCache _siteProfile;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options,
        ISubscriptionEnrollmentStore store, MaxioSiteProfileCache siteProfile, TimeProvider clock, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _store = store;
        _siteProfile = siteProfile;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = CallBudget.Start(cancellationToken, ReadBudget);
        return await ListPlansCoreAsync(budget);
    }

    public async Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        using var budget = CallBudget.Start(cancellationToken, SubscribeBudget);

        // Only a plan this store offers may be subscribed to — not merely any product on the Maxio site.
        var catalog = await ListPlansCoreAsync(budget);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            return new SubscribeResult(SubscribeOutcome.UnknownPlan, Message: $"'{planHandle}' is not an available subscription plan.");
        }

        for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            var enrollment = new SubscriptionEnrollment(subscriber.BuyerId, plan.Handle,
                CustomerReferenceFor(subscriber.BuyerId), NewSubscriptionReference(), _clock.GetUtcNow());

            if (await _store.TryClaimAsync(enrollment, budget.Token))
            {
                return await EnrollAsync(enrollment, subscriber, plan, catalog, budget);
            }

            var existing = await _store.GetAsync(subscriber.BuyerId, budget.Token);
            if (existing is null)
            {
                continue; // released between our claim and this read: claim again
            }

            var result = await ResolveExistingEnrollmentAsync(existing, subscriber, plan, catalog, budget);
            if (result is not null)
            {
                return result;
            }
        }

        return InProgress();
    }

    public async Task<CustomerSubscriptions> ListSubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        using var budget = CallBudget.Start(cancellationToken, ReadBudget);

        var enrollment = await _store.GetAsync(buyerId, budget.Token);
        if (enrollment is not null && enrollment.NeedsSettling(_clock.GetUtcNow(), StalePendingAfter))
        {
            enrollment = await SettleEnrollmentAsync(enrollment);
        }

        var customerId = enrollment?.BillingCustomerId ?? await FindCustomerIdAsync(CustomerReferenceFor(buyerId), budget);
        if (customerId is null)
        {
            return new CustomerSubscriptions(Array.Empty<SubscriptionDetails>(), enrollment?.Status, Truncated: false);
        }

        var subscriptions = (await ListCustomerSubscriptionsAsync(customerId.Value, budget)).ToList();

        // The list operation takes no paging inputs. If it lacks the subscription this store recorded, the
        // list was cut short: add the recorded one and tell the caller.
        var truncated = false;
        if (enrollment?.BillingSubscriptionId is int recordedId && subscriptions.All(s => s.Id != recordedId))
        {
            var recorded = await ReadSubscriptionAsync(recordedId, budget);
            if (recorded is not null)
            {
                subscriptions.Add(recorded);
                truncated = true;
            }
        }

        return new CustomerSubscriptions(
            subscriptions.OrderByDescending(s => s.CreatedAt).Select(ToDetails).ToList(),
            enrollment?.Status,
            truncated);
    }

    private async Task<SubscribeResult?> ResolveExistingEnrollmentAsync(SubscriptionEnrollment existing, Subscriber subscriber,
        SubscriptionPlan plan, SubscriptionPlanCatalog catalog, CallBudget budget)
    {
        var now = _clock.GetUtcNow();
        if (existing.Status == SubscriptionEnrollmentStatus.Pending && !existing.NeedsSettling(now, StalePendingAfter))
        {
            _logger.LogWarning("Subscribe for enrollment {SubscriptionReference} rejected: another request holds the claim.",
                existing.SubscriptionReference);
            return InProgress();
        }

        if (existing.NeedsSettling(now, StalePendingAfter))
        {
            var settled = await SettleEnrollmentAsync(existing);
            if (settled is null)
            {
                return null; // nothing was created: the claim was released, so claim again
            }

            if (settled.Status != SubscriptionEnrollmentStatus.Active)
            {
                return OutcomeUnknown();
            }

            existing = settled;
        }

        var current = existing.BillingSubscriptionId is int subscriptionId
            ? await ReadSubscriptionAsync(subscriptionId, budget)
            : null;

        if (current is null || HasEnded(current))
        {
            // The recorded subscription is over (or gone): re-open the enrollment for this new subscribe.
            existing.Restart(plan.Handle, NewSubscriptionReference(), _clock.GetUtcNow());
            if (!await _store.TryUpdateAsync(existing, budget.Token))
            {
                return InProgress();
            }

            return await EnrollAsync(existing, subscriber, plan, catalog, budget);
        }

        return ExistingSubscriptionResult(current, plan);
    }

    private async Task<SubscribeResult> EnrollAsync(SubscriptionEnrollment enrollment, Subscriber subscriber,
        SubscriptionPlan plan, SubscriptionPlanCatalog catalog, CallBudget budget)
    {
        try
        {
            if (enrollment.BillingCustomerId is null)
            {
                var customerId = await EnsureCustomerAsync(enrollment.CustomerReference, subscriber, budget);
                enrollment.RecordCustomer(customerId, _clock.GetUtcNow());
                if (!await _store.TryUpdateAsync(enrollment, budget.Token))
                {
                    throw new InvalidOperationException("The subscription enrollment was changed by another request.");
                }
            }

            var billingCustomerId = enrollment.BillingCustomerId!.Value;

            // Maxio is the system of record: if it already holds a live subscription to an offered plan for this
            // customer (for example the local enrollment was lost), adopt it instead of creating a second one.
            var live = await FindLiveSubscriptionAsync(billingCustomerId, catalog, budget);
            if (live is not null)
            {
                enrollment.MarkActive(live.Id!.Value, live.Product?.Handle ?? plan.Handle, _clock.GetUtcNow());
                await RecordQuietlyAsync(enrollment);
                _logger.LogInformation("Adopted existing Maxio subscription {SubscriptionId} for customer {CustomerId}.",
                    live.Id, billingCustomerId);
                return ExistingSubscriptionResult(live, plan);
            }

            var created = await CreateSubscriptionAsync(enrollment, billingCustomerId, plan, budget);
            enrollment.MarkActive(created.Id!.Value, plan.Handle, _clock.GetUtcNow());
            await RecordQuietlyAsync(enrollment);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({SubscriptionReference}) on plan {PlanHandle} for customer {CustomerId}; state {State}.",
                created.Id, enrollment.SubscriptionReference, plan.Handle, billingCustomerId, created.State?.Value);
            return new SubscribeResult(SubscribeOutcome.Created, ToDetails(created));
        }
        catch (SubscriptionOutcomeUnknownException ex)
        {
            enrollment.MarkUnknown(_clock.GetUtcNow());
            await RecordQuietlyAsync(enrollment);
            _logger.LogWarning(ex, "Outcome of Maxio subscription create {SubscriptionReference} is unknown; it is settled on the shopper's next request.",
                enrollment.SubscriptionReference);
            return OutcomeUnknown();
        }
        catch
        {
            // No subscription was created (the failure is definite): release the claim so the shopper can retry.
            await _store.TryReleaseAsync(enrollment, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Settles a pending-too-long or unknown enrollment by looking its subscription up by reference.
    /// Returns the enrollment as now recorded, or null when it was released because nothing was created.
    /// </summary>
    private async Task<SubscriptionEnrollment?> SettleEnrollmentAsync(SubscriptionEnrollment enrollment)
    {
        var (outcome, subscription) = await FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference);
        switch (outcome)
        {
            case SettleOutcome.Found:
                enrollment.MarkActive(subscription!.Id!.Value, subscription.Product?.Handle ?? enrollment.PlanHandle, _clock.GetUtcNow());
                _logger.LogInformation("Settled enrollment {SubscriptionReference}: Maxio subscription {SubscriptionId} exists.",
                    enrollment.SubscriptionReference, subscription.Id);
                return await _store.TryUpdateAsync(enrollment, CancellationToken.None) ? enrollment : null;

            case SettleOutcome.NotFound:
                _logger.LogInformation("Settled enrollment {SubscriptionReference}: no Maxio subscription was created; releasing it.",
                    enrollment.SubscriptionReference);
                await _store.TryReleaseAsync(enrollment, CancellationToken.None);
                return null;

            default:
                if (enrollment.Status != SubscriptionEnrollmentStatus.Unknown)
                {
                    enrollment.MarkUnknown(_clock.GetUtcNow());
                    await RecordQuietlyAsync(enrollment);
                }
                return enrollment;
        }
    }

    // ---- Maxio calls -------------------------------------------------------------------------------------

    private async Task<SubscriptionPlanCatalog> ListPlansCoreAsync(CallBudget budget)
    {
        const string operation = "ListProductsForProductFamily";
        var site = await GetSiteProfileAsync(budget);
        var plans = new List<SubscriptionPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _options.ProductFamilyHandle!.Trim(),
                        Page = page,
                        PerPage = PlansPerPage
                    },
                    cancellationToken: budget.Token);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    // Our configured family does not exist on this site: a deployment fault, not the caller's.
                    _logger.LogError(ex, "Maxio product family configured in {Setting} was not found.", $"{MaxioOptions.SectionName}:ProductFamilyHandle");
                    throw new BillingProviderException(BillingFailureKind.Unavailable,
                        "The subscription plan catalog is not available.", (int)ex.StatusCode, innerException: ex);
                }

                throw Translate(operation, ex, budget);
            }
            catch (Exception ex) when (IsProviderFailure(ex, budget))
            {
                throw Translate(operation, ex, budget);
            }

            foreach (var product in batch.Select(r => r.Product))
            {
                if (product is null || product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(product.Handle, product.Name ?? product.Handle, product.Description,
                    product.PriceInCents ?? 0, site.Currency, product.Interval, product.IntervalUnit?.Value));
            }

            if (batch.Count < PlansPerPage)
            {
                break;
            }

            if (page >= MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Plan listing stopped at {MaxPages} pages of {PerPage}; the catalog returned is partial.",
                    MaxPlanPages, PlansPerPage);
                break;
            }
        }

        return new SubscriptionPlanCatalog(plans, truncated);
    }

    private async Task<MaxioSiteProfile> GetSiteProfileAsync(CallBudget budget)
    {
        const string operation = "ReadSite";
        if (_siteProfile.Current is { } cached)
        {
            return cached;
        }

        SiteResponse response;
        try
        {
            response = await _client.Sites.ReadSite(cancellationToken: budget.Token);
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(operation, ex, budget);
        }

        var site = response.Site ?? throw Unreadable(operation);
        CollectionMethod invoiceMethod;
        if (site.RelationshipInvoicingEnabled is bool relationshipInvoicing)
        {
            invoiceMethod = relationshipInvoicing ? CollectionMethod.Remittance : CollectionMethod.Invoice;
        }
        else
        {
            // Not reported: Relationship Invoicing is the current architecture, whose invoice-style method is remittance.
            invoiceMethod = CollectionMethod.Remittance;
            _logger.LogWarning("Maxio site did not report its invoicing architecture; assuming Relationship Invoicing.");
        }

        var profile = new MaxioSiteProfile(invoiceMethod, site.Currency);
        _siteProfile.Set(profile);
        _logger.LogInformation("Maxio site profile loaded: collection method {CollectionMethod}, currency {Currency}.",
            invoiceMethod.Value, site.Currency);
        return profile;
    }

    /// <summary>The Maxio customer id for a reference, or null when Maxio has no customer with it.</summary>
    private async Task<int?> FindCustomerIdAsync(string reference, CallBudget budget)
    {
        const string operation = "ReadCustomerByReference";
        CustomerResponse response;
        try
        {
            response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: budget.Token);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(operation, ex, budget);
        }

        // An answer without an id is unreadable, not "no customer" — treating it as absent would create a duplicate.
        return response.Customer?.Id ?? throw Unreadable(operation);
    }

    private async Task<int> EnsureCustomerAsync(string reference, Subscriber subscriber, CallBudget budget)
    {
        const string operation = "CreateCustomer";
        if (await FindCustomerIdAsync(reference, budget) is int existingId)
        {
            return existingId;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = subscriber.FirstName,
                            LastName = subscriber.LastName,
                            Email = subscriber.Email,
                            // Maxio allows one customer per reference: this is what makes the create idempotent.
                            Reference = reference
                        }
                    }
                },
                cancellationToken: budget.Token);

            if (response.Customer?.Id is int createdId)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId}.", createdId);
                return createdId;
            }

            return await SettleCustomerAsync(reference, operation, cause: null);
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode < 500)
        {
            // A concurrent create with the same reference is refused too; if the customer now exists, use it.
            if (await TryFindCustomerAfterFailureAsync(reference) is int racedId)
            {
                return racedId;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                var errors = ReadCustomerErrors(body);
                _logger.LogWarning("Maxio refused {Operation} with {StatusCode}: {Errors}", operation, (int)ex.StatusCode, string.Join("; ", errors));
                throw new BillingProviderException(BillingFailureKind.Rejected,
                    "The billing system rejected the customer details.", (int)ex.StatusCode, errors, ex);
            }

            // Any other 4xx: TryGetRawError is the remaining accessor; Translate reads it for the status and errors.
            throw Translate(operation, ex, budget);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            // The create may have landed: settle by the reference it carried.
            return await SettleCustomerAsync(reference, operation, ex);
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(operation, ex, budget);
        }
    }

    private async Task<int> SettleCustomerAsync(string reference, string operation, Exception? cause)
    {
        if (await TryFindCustomerAfterFailureAsync(reference) is int id)
        {
            _logger.LogInformation("Settled ambiguous {Operation}: Maxio customer {CustomerId} exists.", operation, id);
            return id;
        }

        // Not found (or still unreadable). No subscription has been created yet, so the claim is released and the
        // next attempt starts with the same reference lookup — a customer that did land is found then.
        throw cause is null
            ? Unreadable(operation)
            : Translate(operation, cause, budget: null);
    }

    private async Task<int?> TryFindCustomerAfterFailureAsync(string reference)
    {
        using var settle = CallBudget.Start(CancellationToken.None, SettleBudget);
        try
        {
            return await FindCustomerIdAsync(reference, settle);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(ex, "Could not re-read Maxio customer by reference after a failed create.");
            return null;
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, SubscriptionPlanCatalog catalog, CallBudget budget)
    {
        var offered = new HashSet<string>(catalog.Plans.Select(p => p.Handle), StringComparer.Ordinal);
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, budget);
        return subscriptions
            .Where(s => s.Product?.Handle is string handle && offered.Contains(handle) && !HasEnded(s))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CallBudget budget)
    {
        const string operation = "ListCustomerSubscriptions";
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: budget.Token);

            return responses
                .Select(r => r.Subscription)
                .Where(s => s?.Id is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(operation, ex, budget);
        }
    }

    /// <summary>The subscription, or null when Maxio no longer has it.</summary>
    private async Task<Subscription?> ReadSubscriptionAsync(int subscriptionId, CallBudget budget)
    {
        const string operation = "ReadSubscription";
        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId },
                cancellationToken: budget.Token);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(operation, ex, budget);
        }

        return response.Subscription is { Id: not null } subscription ? subscription : throw Unreadable(operation);
    }

    private async Task<Subscription> CreateSubscriptionAsync(SubscriptionEnrollment enrollment, int customerId,
        SubscriptionPlan plan, CallBudget budget)
    {
        const string operation = "CreateSubscription";
        var site = await GetSiteProfileAsync(budget);
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            // No payment method is captured by this store: bill by invoice instead of charging a card on file.
                            PaymentCollectionMethod = site.InvoiceCollectionMethod,
                            // Stored on the enrollment before this call: how an ambiguous outcome is found again.
                            Reference = enrollment.SubscriptionReference
                        }
                    }
                },
                cancellationToken: budget.Token);

            if (response.Subscription is { Id: not null } created)
            {
                return created;
            }

            return await SettleSubscriptionCreateAsync(enrollment.SubscriptionReference, cause: null);
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode < 500)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                var errors = body.Errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() ?? new List<string>();
                _logger.LogWarning("Maxio refused {Operation} for {SubscriptionReference} with {StatusCode}: {Errors}",
                    operation, enrollment.SubscriptionReference, (int)ex.StatusCode, string.Join("; ", errors));
                throw new BillingProviderException(BillingFailureKind.Rejected,
                    "The billing system rejected the subscription.", (int)ex.StatusCode, errors, ex);
            }

            // Any other 4xx: TryGetRawError is the remaining accessor; Translate reads it for the status and errors.
            throw Translate(operation, ex, budget);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            // The create may have landed: settle by the reference it carried before reporting anything.
            return await SettleSubscriptionCreateAsync(enrollment.SubscriptionReference, ex);
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(operation, ex, budget);
        }
    }

    private async Task<Subscription> SettleSubscriptionCreateAsync(string reference, Exception? cause)
    {
        var (outcome, subscription) = await FindSubscriptionByReferenceAsync(reference);
        switch (outcome)
        {
            case SettleOutcome.Found:
                _logger.LogInformation("Settled ambiguous CreateSubscription {SubscriptionReference}: subscription {SubscriptionId} exists.",
                    reference, subscription!.Id);
                return subscription;

            case SettleOutcome.NotFound:
                // Definitely not created: report the original failure; the caller releases the claim.
                throw cause is null ? Unreadable("CreateSubscription") : Translate("CreateSubscription", cause, budget: null);

            default:
                throw new SubscriptionOutcomeUnknownException(reference, cause);
        }
    }

    private async Task<(SettleOutcome Outcome, Subscription? Subscription)> FindSubscriptionByReferenceAsync(string reference)
    {
        using var settle = CallBudget.Start(CancellationToken.None, SettleBudget);
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: settle.Token);

            return response.Subscription is { Id: not null } found
                ? (SettleOutcome.Found, found)
                : (SettleOutcome.Unknown, null);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return (SettleOutcome.NotFound, null);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning(ex, "FindSubscription for {SubscriptionReference} failed with {StatusCode}.", reference, (int)raw.StatusCode);
            }

            return (SettleOutcome.Unknown, null);
        }
        catch (Exception ex) when (ex is SdkException || ex is OperationCanceledException)
        {
            _logger.LogWarning(ex, "FindSubscription for {SubscriptionReference} failed; outcome stays unknown.", reference);
            return (SettleOutcome.Unknown, null);
        }
    }

    // ---- Error translation -------------------------------------------------------------------------------

    /// <summary>
    /// Everything the SDK raises, plus our own budget expiring. The caller's own cancellation is not a provider
    /// failure and propagates untouched.
    /// </summary>
    private static bool IsProviderFailure(Exception ex, CallBudget budget) =>
        ex is SdkException || (ex is OperationCanceledException && !budget.CallerCancelled);

    /// <summary>A write failure after which the provider may still have acted.</summary>
    private static bool IsAmbiguousWriteFailure(Exception ex) => ex switch
    {
        SdkConnectionException => true,                                   // includes SdkTimeoutException
        ResponseDeserializationException d => (int)d.StatusCode is >= 200 and < 300,
        ApiException api => (int)api.StatusCode >= 500,
        OperationCanceledException => true,                               // budget or caller cancelled mid-write
        _ => false
    };

    private BillingProviderException Translate(string operation, Exception ex, CallBudget? budget)
    {
        switch (ex)
        {
            case ResponseDeserializationException d:
                _logger.LogError(ex, "Maxio {Operation} returned {StatusCode} with a body that could not be read.", operation, (int)d.StatusCode);
                return (int)d.StatusCode is >= 400 and < 500 && (int)d.StatusCode is not (401 or 403 or 404 or 429)
                    ? new BillingProviderException(BillingFailureKind.Rejected, "The billing system rejected the request.", (int)d.StatusCode, innerException: ex)
                    : new BillingProviderException(BillingFailureKind.Unavailable, "The billing system returned a response that could not be processed.", (int)d.StatusCode, innerException: ex);

            case ApiException api:
                var status = (int)api.StatusCode;
                if (status is 401 or 403)
                {
                    _logger.LogError(ex, "Maxio refused our credentials on {Operation} ({StatusCode}). Check {Setting}.", operation, status, $"{MaxioOptions.SectionName}:ApiKey");
                    return new BillingProviderException(BillingFailureKind.Unavailable, "The billing system is unavailable.", status, innerException: ex);
                }

                if (status is 429 || status >= 500 || status == 404)
                {
                    _logger.LogError(ex, "Maxio {Operation} failed with {StatusCode}.", operation, status);
                    return new BillingProviderException(BillingFailureKind.Unavailable, "The billing system is unavailable.", status, innerException: ex);
                }

                var errors = ReadRawErrors(api);
                _logger.LogWarning(ex, "Maxio refused {Operation} with {StatusCode}: {Errors}", operation, status, string.Join("; ", errors));
                return new BillingProviderException(BillingFailureKind.Rejected, "The billing system rejected the request.", status, errors, ex);

            case SdkTimeoutException:
                _logger.LogError(ex, "Maxio {Operation} did not answer in time.", operation);
                return new BillingProviderException(BillingFailureKind.Timeout, "The billing system did not respond in time.", innerException: ex);

            case SdkConnectionException:
                _logger.LogError(ex, "Maxio {Operation} could not be reached.", operation);
                return new BillingProviderException(BillingFailureKind.Unavailable, "The billing system could not be reached.", innerException: ex);

            case AuthSchemeException:
                _logger.LogError(ex, "Maxio credentials could not be applied on {Operation}.", operation);
                return new BillingProviderException(BillingFailureKind.Unavailable, "The billing system is unavailable.", innerException: ex);

            case OperationCanceledException when budget is null || !budget.CallerCancelled:
                _logger.LogError(ex, "Maxio {Operation} exceeded its time budget.", operation);
                return new BillingProviderException(BillingFailureKind.Timeout, "The billing system did not respond in time.", innerException: ex);

            default:
                _logger.LogError(ex, "Maxio {Operation} failed.", operation);
                return new BillingProviderException(BillingFailureKind.Unavailable, "The billing system is unavailable.", innerException: ex);
        }
    }

    private BillingProviderException Unreadable(string operation)
    {
        _logger.LogError("Maxio {Operation} succeeded without the record it should return.", operation);
        return new BillingProviderException(BillingFailureKind.Unavailable, "The billing system returned a response that could not be processed.");
    }

    private static IReadOnlyList<string> ReadCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return Array.Empty<string>();
        }

        if (body.Errors.TryGetListOfString(out var list))
        {
            return list.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        }

        if (body.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
        {
            return new[] { customerError.Customer! };
        }

        return Array.Empty<string>();
    }

    /// <summary>Best-effort error strings from an untyped error body (Maxio sends <c>{"errors": [...]}</c>).</summary>
    private static IReadOnlyList<string> ReadRawErrors(ApiException ex)
    {
        RawError? raw = ex switch
        {
            ApiException<RawError> r => r.Error,
            ApiException<CreateCustomerError> c when c.Error.TryGetRawError(out var cr) => cr,
            ApiException<CreateSubscriptionError> s when s.Error.TryGetRawError(out var sr) => sr,
            ApiException<ListProductsForProductFamilyError> p when p.Error.TryGetRawError(out var pr) => pr,
            ApiException<FindSubscriptionError> f when f.Error.TryGetRawError(out var fr) => fr,
            _ => null
        };

        if (raw is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            var body = raw.ReadAsJson<ErrorListResponse1>();
            return body?.Errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>(); // not JSON (or not that shape): keep the status, drop the detail
        }
    }

    // ---- Mapping & helpers -------------------------------------------------------------------------------

    private static bool HasEnded(Subscription subscription) =>
        subscription.State is not null && EndedStates.Any(state => state == subscription.State);

    private static SubscribeResult ExistingSubscriptionResult(Subscription subscription, SubscriptionPlan requested)
    {
        var details = ToDetails(subscription);
        return string.Equals(subscription.Product?.Handle, requested.Handle, StringComparison.Ordinal)
            ? new SubscribeResult(SubscribeOutcome.AlreadySubscribed, details)
            : new SubscribeResult(SubscribeOutcome.SubscribedToDifferentPlan, details,
                $"This account already has an active subscription to '{subscription.Product?.Handle}'.");
    }

    private static SubscribeResult InProgress() =>
        new(SubscribeOutcome.InProgress, Message: "A subscribe request for this account is already in progress. Try again shortly.");

    private static SubscribeResult OutcomeUnknown() =>
        new(SubscribeOutcome.OutcomeUnknown, Message: "The billing system has not confirmed the subscription yet. Check your subscriptions shortly.");

    internal static SubscriptionDetails ToDetails(Subscription s) => new(
        s.Id!.Value,
        s.Reference,
        s.Product?.Handle,
        s.Product?.Name,
        s.State?.Value ?? "unknown",
        s.ProductPriceInCents,
        s.Currency,
        s.Product?.Interval,
        s.Product?.IntervalUnit?.Value,
        NextBillingAt: s.CurrentPeriodEndsAt,
        NextAssessmentAt: s.NextAssessmentAt,
        ActivatedAt: s.ActivatedAt,
        CreatedAt: s.CreatedAt);

    /// <summary>
    /// The Maxio customer reference for a buyer: stable for the buyer, namespaced to this store, and free of the
    /// buyer's personal data (it travels in URLs and logs).
    /// </summary>
    internal static string CustomerReferenceFor(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId.Trim().ToUpperInvariant()));
        return "eshop-user-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private static string NewSubscriptionReference() => "eshop-sub-" + Guid.NewGuid().ToString("N");

    private async Task RecordQuietlyAsync(SubscriptionEnrollment enrollment)
    {
        // Maxio already acted; failing to record it locally must not turn that into an error for the shopper.
        // A later request settles the enrollment from Maxio.
        if (!await _store.TryUpdateAsync(enrollment, CancellationToken.None))
        {
            _logger.LogWarning("Enrollment {SubscriptionReference} changed concurrently; its record was not updated.", enrollment.SubscriptionReference);
        }
    }

    private enum SettleOutcome
    {
        Found,
        NotFound,
        Unknown
    }

    private sealed class SubscriptionOutcomeUnknownException : Exception
    {
        public SubscriptionOutcomeUnknownException(string reference, Exception? cause)
            : base($"The outcome of subscription create {reference} is unknown.", cause)
        {
        }
    }

    /// <summary>The single home of a call's total time budget, linked to the caller's own cancellation.</summary>
    private sealed class CallBudget : IDisposable
    {
        private readonly CancellationTokenSource _source;
        private readonly CancellationToken _callerToken;

        private CallBudget(CancellationToken callerToken, TimeSpan budget)
        {
            _callerToken = callerToken;
            _source = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            _source.CancelAfter(budget);
        }

        public static CallBudget Start(CancellationToken callerToken, TimeSpan budget) => new(callerToken, budget);

        public CancellationToken Token => _source.Token;
        public bool CallerCancelled => _callerToken.IsCancellationRequested;

        public void Dispose() => _source.Dispose();
    }
}
