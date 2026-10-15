using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing (the system of record for plans and subscriptions).
/// Subscribe order is always: claim (a row keyed by the user, refused by the store on a duplicate) →
/// Maxio writes → record the result. A write whose outcome is unknown is settled by re-reading Maxio.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>Total time one API request may spend on Maxio calls (the per-attempt timeout is set on the client).</summary>
    internal static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(30);

    /// <summary>Separate budget for re-reading after a create whose outcome is unknown (the request budget may be spent).</summary>
    internal static readonly TimeSpan SettlementBudget = TimeSpan.FromSeconds(15);

    /// <summary>A pending claim older than this belongs to an attempt that died; it may be taken over and settled.</summary>
    internal static readonly TimeSpan StalePendingAfter = TimeSpan.FromMinutes(2);

    /// <summary>Tolerance between our clock and Maxio's when matching a subscription by creation time.</summary>
    private static readonly TimeSpan ProviderClockSkew = TimeSpan.FromMinutes(5);

    internal const int PlansPerPage = 200;
    internal const int MaxPlanPages = 5;

    private static readonly SubscriptionState[] EndedStates =
    [
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate
    ];

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(MaxioAdvancedBillingClient client, CatalogContext db,
        IOptions<MaxioSettings> settings, TimeProvider time, ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = StartBudget(cancellationToken);
        try
        {
            return await ReadPlanCatalogAsync(budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw BudgetExpired();
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrEmpty(subscriber.UserId, nameof(subscriber.UserId));
        Guard.Against.NullOrEmpty(subscriber.NormalizedUserName, nameof(subscriber.NormalizedUserName));
        Guard.Against.NullOrEmpty(subscriber.Email, nameof(subscriber.Email));
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.PlanNotOffered, "A plan handle is required.");
        }

        using var budget = StartBudget(cancellationToken);
        try
        {
            // Invariant: only a plan the configured product family offers may be subscribed to.
            var plan = await ResolveOfferedPlanAsync(planHandle.Trim(), budget.Token);
            return await EnrollAsync(subscriber, plan, budget.Token, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw BudgetExpired();
        }
    }

    public async Task<CustomerSubscriptions> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrEmpty(subscriber.UserId, nameof(subscriber.UserId));
        Guard.Against.NullOrEmpty(subscriber.NormalizedUserName, nameof(subscriber.NormalizedUserName));

        using var budget = StartBudget(cancellationToken);
        try
        {
            var enrollment = await _db.SubscriptionEnrollments
                .SingleOrDefaultAsync(e => e.BuyerId == subscriber.UserId, budget.Token);

            if (enrollment is { Status: SubscriptionEnrollmentStatus.Unknown })
            {
                await TrySettleUnknownAsync(enrollment, budget.Token);
            }

            var customerId = enrollment?.BillingCustomerId
                ?? await ReadCustomerIdByReferenceAsync(CustomerReference(subscriber), budget.Token);

            var awaitingConfirmation = enrollment?.IsAwaitingConfirmation ?? false;
            if (customerId is not int id)
            {
                return new CustomerSubscriptions(Array.Empty<SubscriptionDetails>(), awaitingConfirmation);
            }

            var subscriptions = (await ListCustomerSubscriptionsAsync(id, budget.Token))
                .Select(ToDetails)
                .OfType<SubscriptionDetails>()
                .OrderByDescending(s => s.CreatedAt)
                .ToList();

            return new CustomerSubscriptions(subscriptions, awaitingConfirmation);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw BudgetExpired();
        }
    }

    // ---------------------------------------------------------------- plans

    private async Task<SubscriptionPlan> ResolveOfferedPlanAsync(string planHandle, CancellationToken ct)
    {
        var catalog = await ReadPlanCatalogAsync(ct);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is not null)
        {
            return plan;
        }

        if (catalog.IsTruncated)
        {
            // An incomplete list is not proof the plan is not offered.
            _logger.LogError("Plan listing for product family {ProductFamily} hit the {MaxPages}-page cap; cannot confirm a requested plan.",
                _settings.ProductFamilyHandle, MaxPlanPages);
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                "The requested plan could not be confirmed. Please try again later.");
        }

        throw new SubscriptionBillingException(SubscriptionBillingErrorKind.PlanNotOffered,
            "The requested plan is not an available subscription plan.");
    }

    private async Task<SubscriptionPlanCatalog> ReadPlanCatalogAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var seenHandles = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;

        for (var page = 1; ; page++)
        {
            var products = await ListProductsPageAsync(page, ct);

            var newOnPage = 0;
            foreach (var product in products.Select(p => p.Product))
            {
                if (product?.Handle is not { Length: > 0 } handle || !seenHandles.Add(handle))
                {
                    continue;
                }

                newOnPage++;
                if (product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    handle,
                    product.Name ?? handle,
                    product.Description,
                    product.PriceInCents ?? 0,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.RequireCreditCard ?? false));
            }

            if (products.Count < PlansPerPage)
            {
                break;
            }

            if (newOnPage == 0 || page >= MaxPlanPages)
            {
                // A full page that brought nothing new, or the page cap: stop without trusting the provider to end it.
                truncated = true;
                _logger.LogWarning("Plan listing for product family {ProductFamily} stopped at page {Page}; result is incomplete.",
                    _settings.ProductFamilyHandle, page);
                break;
            }
        }

        return new SubscriptionPlanCatalog(plans, truncated);
    }

    // ---------------------------------------------------------------- subscribe

    private enum ClaimKind { Fresh, Resumed, Active }

    private async Task<SubscribeResult> EnrollAsync(Subscriber subscriber, SubscriptionPlan plan,
        CancellationToken ct, CancellationToken callerToken)
    {
        var (enrollment, kind) = await ClaimAsync(subscriber, plan, ct);

        if (kind == ClaimKind.Active)
        {
            var current = await ReadEnrolledSubscriptionAsync(subscriber, enrollment, ct);
            if (current is not null && IsLive(current))
            {
                return SameOrConflict(current, plan, SubscribeOutcome.AlreadySubscribed);
            }

            // The previous subscription has ended: start a fresh attempt on the same claim row.
            enrollment.BeginAttempt(plan.Handle, NewSubscriptionReference(), _time.GetUtcNow());
            await SaveClaimAsync(enrollment);
            kind = ClaimKind.Fresh;
        }

        var createSent = false;
        try
        {
            var customerId = await EnsureCustomerAsync(subscriber, enrollment, ct);

            // Adopt a subscription that already exists for this customer (an earlier unsettled attempt, or
            // an enrollment row lost with an in-memory store) instead of creating a second one.
            var (existing, fromThisAttempt) = await FindExistingSubscriptionAsync(enrollment, kind, customerId, ct);
            if (existing?.Id is int existingId)
            {
                enrollment.MarkActive(existingId, existing.Product?.Handle ?? plan.Handle, _time.GetUtcNow());
                await SaveClaimAsync(enrollment);
                return SameOrConflict(existing, plan, fromThisAttempt ? SubscribeOutcome.Created : SubscribeOutcome.AlreadySubscribed);
            }

            createSent = true;
            return await CreateSubscriptionAsync(enrollment, customerId, plan, ct, callerToken);
        }
        catch (Exception) when (!createSent && enrollment.Status == SubscriptionEnrollmentStatus.Pending)
        {
            // Nothing billable was sent by this attempt. A resumed attempt may still have an earlier create in
            // flight under its reference, so it goes back to Unknown; a fresh claim is released for a clean retry.
            if (kind == ClaimKind.Resumed)
            {
                enrollment.MarkUnknown(_time.GetUtcNow());
                await SaveClaimQuietlyAsync(enrollment);
            }
            else
            {
                await ReleaseClaimAsync(enrollment);
            }

            throw;
        }
    }

    private async Task<(SubscriptionEnrollment Enrollment, ClaimKind Kind)> ClaimAsync(
        Subscriber subscriber, SubscriptionPlan plan, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var claim = new SubscriptionEnrollment(subscriber.UserId, plan.Handle, NewSubscriptionReference(), now);
        _db.SubscriptionEnrollments.Add(claim);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
            return (claim, ClaimKind.Fresh);
        }
        catch (Exception ex) when (IsPossibleDuplicateClaim(ex))
        {
            _db.ChangeTracker.Clear();
            _logger.LogInformation(ex, "Subscription claim for user {UserId} was refused by the store; inspecting it.", subscriber.UserId);
        }

        // The store refused the insert. A row for this user means another request owns or owned the claim;
        // no row means the refusal was something else, so it is not treated as a duplicate.
        var existing = await _db.SubscriptionEnrollments.SingleOrDefaultAsync(e => e.BuyerId == subscriber.UserId, ct)
            ?? throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unavailable,
                "Your subscription request could not be recorded. Please try again.");

        switch (existing.Status)
        {
            case SubscriptionEnrollmentStatus.Active:
                return (existing, ClaimKind.Active);
            case SubscriptionEnrollmentStatus.Pending when !existing.IsStale(now, StalePendingAfter):
                _logger.LogWarning("Duplicate subscribe for user {UserId} refused: attempt in progress.", subscriber.UserId);
                throw InProgress();
            default:
                // Unknown, or a pending attempt that died: take it over (the concurrency token lets only one caller win).
                existing.ResumeAttempt(now);
                await SaveClaimAsync(existing);
                _logger.LogInformation("Resuming unsettled subscribe attempt {Reference} for user {UserId}.",
                    existing.SubscriptionReference, subscriber.UserId);
                return (existing, ClaimKind.Resumed);
        }
    }

    private async Task<int> EnsureCustomerAsync(Subscriber subscriber, SubscriptionEnrollment enrollment, CancellationToken ct)
    {
        if (enrollment.BillingCustomerId is int known)
        {
            return known;
        }

        var reference = CustomerReference(subscriber);
        var customerId = await ReadCustomerIdByReferenceAsync(reference, ct)
            ?? await CreateCustomerAsync(subscriber, reference, ct);

        enrollment.RecordBillingCustomer(customerId, _time.GetUtcNow());
        await SaveClaimAsync(enrollment);
        return customerId;
    }

    private async Task<(Subscription? Subscription, bool FromThisAttempt)> FindExistingSubscriptionAsync(
        SubscriptionEnrollment enrollment, ClaimKind kind, int customerId, CancellationToken ct)
    {
        if (kind == ClaimKind.Resumed)
        {
            var byReference = await FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, ct);
            if (byReference is not null && IsLive(byReference))
            {
                return (byReference, true);
            }
        }

        var live = (await ListCustomerSubscriptionsAsync(customerId, ct))
            .Where(IsLive)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();

        var adopted = live.FirstOrDefault(s => s.Product?.Handle == enrollment.PlanHandle) ?? live.FirstOrDefault();
        return (adopted, false);
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(SubscriptionEnrollment enrollment, int customerId,
        SubscriptionPlan plan, CancellationToken ct, CancellationToken callerToken)
    {
        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        Reference = enrollment.SubscriptionReference,
                        // No card is captured: Maxio invoices the shopper instead of charging a payment method on file
                        // (with the default automatic collection Maxio rejects the signup: no payment method on file).
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                }
            }, cancellationToken: ct);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            // A definite refusal: nothing was created, so release the claim.
            await ReleaseClaimAsync(enrollment);
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                _logger.LogWarning(ex, "Maxio rejected CreateSubscription for plan {Plan} (HTTP {StatusCode}): {Errors}",
                    plan.Handle, (int)ex.StatusCode, string.Join("; ", errorList.Errors));
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Rejected,
                    "The billing provider declined the subscription.", errorList.Errors, ex);
            }

            if (ex.Error.TryGetRawError(out RawError _))
            {
                throw FromProviderStatus(nameof(_client.Subscriptions.CreateSubscription), ex.StatusCode, ex);
            }

            throw FromProviderStatus(nameof(_client.Subscriptions.CreateSubscription), ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex) when (!IsSuccess(ex.StatusCode))
        {
            // Refused, but the error body did not match its declared shape.
            await ReleaseClaimAsync(enrollment);
            throw FromProviderStatus(nameof(_client.Subscriptions.CreateSubscription), ex.StatusCode, ex);
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseClaimAsync(enrollment);
            throw Translate(nameof(_client.Subscriptions.CreateSubscription), ex);
        }
        catch (Exception ex) when (ex is SdkConnectionException or ResponseDeserializationException or OperationCanceledException)
        {
            // Transport failure, timeout, budget expiry or an unreadable 2xx: Maxio may have created it.
            _logger.LogError(ex, "Outcome of CreateSubscription {Reference} for user {UserId} is unknown; re-reading Maxio.",
                enrollment.SubscriptionReference, enrollment.BuyerId);
            var settled = await SettleCreateAsync(enrollment, customerId, plan);
            if (ex is OperationCanceledException && callerToken.IsCancellationRequested)
            {
                throw;
            }

            return settled;
        }

        if (response.Subscription is not { Id: int subscriptionId } subscription)
        {
            _logger.LogError("CreateSubscription {Reference} returned no subscription id; re-reading Maxio.", enrollment.SubscriptionReference);
            return await SettleCreateAsync(enrollment, customerId, plan);
        }

        enrollment.MarkActive(subscriptionId, subscription.Product?.Handle ?? plan.Handle, _time.GetUtcNow());
        await SaveClaimQuietlyAsync(enrollment);
        _logger.LogInformation("User {UserId} subscribed to plan {Plan}: Maxio subscription {SubscriptionId} ({State}).",
            enrollment.BuyerId, plan.Handle, subscriptionId, subscription.State?.Value);

        return new SubscribeResult(SubscribeOutcome.Created, ToDetails(subscription));
    }

    /// <summary>Settles a create whose outcome is unknown, before the request returns. Unsettled → recorded as Unknown.</summary>
    private async Task<SubscribeResult> SettleCreateAsync(SubscriptionEnrollment enrollment, int customerId, SubscriptionPlan plan)
    {
        using var settle = new CancellationTokenSource(SettlementBudget);
        try
        {
            var found = await FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, settle.Token);
            if (found is null)
            {
                var since = enrollment.AttemptStartedAt - ProviderClockSkew;
                found = (await ListCustomerSubscriptionsAsync(customerId, settle.Token))
                    .Where(s => IsLive(s) && s.Product?.Handle == plan.Handle && s.CreatedAt >= since)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefault();
            }

            if (found?.Id is int id)
            {
                enrollment.MarkActive(id, found.Product?.Handle ?? plan.Handle, _time.GetUtcNow());
                await SaveClaimQuietlyAsync(enrollment);
                _logger.LogInformation("Unknown CreateSubscription {Reference} settled: Maxio subscription {SubscriptionId} exists.",
                    enrollment.SubscriptionReference, id);
                return new SubscribeResult(SubscribeOutcome.Created, ToDetails(found));
            }
        }
        catch (Exception ex) when (ex is SubscriptionBillingException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Re-read of CreateSubscription {Reference} failed.", enrollment.SubscriptionReference);
        }

        enrollment.MarkUnknown(_time.GetUtcNow());
        await SaveClaimQuietlyAsync(enrollment);
        _logger.LogError("CreateSubscription {Reference} for user {UserId} remains unconfirmed; it will be settled on the next request.",
            enrollment.SubscriptionReference, enrollment.BuyerId);
        return new SubscribeResult(SubscribeOutcome.Unconfirmed, null);
    }

    private async Task TrySettleUnknownAsync(SubscriptionEnrollment enrollment, CancellationToken ct)
    {
        try
        {
            var found = await FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, ct);
            if (found?.Id is int id && IsLive(found))
            {
                enrollment.MarkActive(id, found.Product?.Handle ?? enrollment.PlanHandle, _time.GetUtcNow());
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // A subscribe request took the claim over meanwhile; it settles the attempt itself.
            _db.ChangeTracker.Clear();
        }
        catch (SubscriptionBillingException ex)
        {
            _logger.LogWarning(ex, "Could not settle subscribe attempt {Reference} yet.", enrollment.SubscriptionReference);
        }
    }

    private async Task<Subscription?> ReadEnrolledSubscriptionAsync(Subscriber subscriber, SubscriptionEnrollment enrollment, CancellationToken ct)
    {
        var customerId = enrollment.BillingCustomerId
            ?? await ReadCustomerIdByReferenceAsync(CustomerReference(subscriber), ct);
        if (customerId is not int id)
        {
            return null;
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(id, ct);
        return subscriptions.FirstOrDefault(s => s.Id == enrollment.BillingSubscriptionId);
    }

    private SubscribeResult SameOrConflict(Subscription subscription, SubscriptionPlan requested, SubscribeOutcome outcome)
    {
        if (subscription.Product?.Handle == requested.Handle)
        {
            return new SubscribeResult(outcome, ToDetails(subscription));
        }

        throw new SubscriptionBillingException(SubscriptionBillingErrorKind.AlreadySubscribed,
            "You already have an active subscription to a different plan. Changing plans is not supported.",
            new[] { $"Current plan: {subscription.Product?.Handle}" });
    }

    // ---------------------------------------------------------------- claim persistence

    private async Task SaveClaimAsync(SubscriptionEnrollment enrollment)
    {
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request changed the claim since we read it: it owns the attempt now.
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Subscription claim for user {UserId} was taken over by another request.", enrollment.BuyerId);
            throw InProgress();
        }
    }

    /// <summary>Records an outcome that already happened at Maxio; a lost race here must not turn success into failure.</summary>
    private async Task SaveClaimQuietlyAsync(SubscriptionEnrollment enrollment)
    {
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Could not record subscription state {Status} for user {UserId}.", enrollment.Status, enrollment.BuyerId);
        }
    }

    private async Task ReleaseClaimAsync(SubscriptionEnrollment enrollment)
    {
        try
        {
            _db.SubscriptionEnrollments.Remove(enrollment);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Could not release subscription claim for user {UserId}.", enrollment.BuyerId);
        }
    }

    /// <summary>
    /// How the store refuses a second claim with the same primary key: a relational provider (SQL Server) raises a
    /// DbUpdateException; the EF Core in-memory provider raises ArgumentException ("An item with the same key has
    /// already been added") — pinned by SubscriptionClaimTests. The caller confirms by reading the existing row.
    /// </summary>
    private bool IsPossibleDuplicateClaim(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => false,
        DbUpdateException => true,
        ArgumentException => _db.Database.IsInMemory(),
        _ => false
    };

    // ---------------------------------------------------------------- Maxio calls

    private async Task<IReadOnlyList<ProductResponse>> ListProductsPageAsync(int page, CancellationToken ct)
    {
        const string operation = nameof(_client.ProductFamilies.ListProductsForProductFamily);
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
            {
                ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                Page = page,
                PerPage = PlansPerPage
            }, cancellationToken: ct);
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                _logger.LogError(ex, "Maxio product family {ProductFamily} was not found ({Detail}); check Maxio:ProductFamilyHandle.",
                    _settings.ProductFamilyHandle, notFound);
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "Subscription plans are not available right now.", innerException: ex);
            }

            if (ex.Error.TryGetRawError(out RawError _))
            {
                throw FromProviderStatus(operation, ex.StatusCode, ex);
            }

            throw FromProviderStatus(operation, ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    /// <returns>The Maxio customer id, or null when Maxio has no customer with this reference.</returns>
    private async Task<int?> ReadCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        const string operation = nameof(_client.Customers.ReadCustomerByReference);
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return response.Customer?.Id
                ?? throw new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "The billing provider returned an incomplete customer record.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Only a definite 404 means "absent"; anything else is unknown and must never lead to a create.
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    private async Task<int> CreateCustomerAsync(Subscriber subscriber, string reference, CancellationToken ct)
    {
        const string operation = nameof(_client.Customers.CreateCustomer);
        var (firstName, lastName) = DeriveNames(subscriber.Email);
        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = subscriber.Email,
                        // Maxio allows one customer per reference, which makes this create idempotent per user.
                        Reference = reference
                    }
                }
            }, cancellationToken: ct);

            return response.Customer?.Id
                ?? throw new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "The billing provider returned an incomplete customer record.");
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                var details = CustomerErrors(body);
                // A concurrent create with the same reference won: adopt that customer.
                var adopted = await ReadCustomerIdByReferenceAsync(reference, ct);
                if (adopted is int adoptedId)
                {
                    return adoptedId;
                }

                _logger.LogWarning(ex, "Maxio rejected CreateCustomer {Reference} (HTTP {StatusCode}): {Errors}",
                    reference, (int)ex.StatusCode, string.Join("; ", details));
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Rejected,
                    "The billing provider could not create a billing account for this user.", details, ex);
            }

            if (ex.Error.TryGetRawError(out RawError _))
            {
                throw FromProviderStatus(operation, ex.StatusCode, ex);
            }

            throw FromProviderStatus(operation, ex.StatusCode, ex);
        }
        catch (Exception ex) when (ex is SdkConnectionException
                                   || (ex is ResponseDeserializationException rde && IsSuccess(rde.StatusCode)))
        {
            // The customer may have been created: re-read by the reference that was sent.
            _logger.LogWarning(ex, "Outcome of CreateCustomer {Reference} is unknown; re-reading.", reference);
            using var settle = new CancellationTokenSource(SettlementBudget);
            var found = await ReadCustomerIdByReferenceAsync(reference, settle.Token);
            return found ?? throw Translate(operation, (SdkException)ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
            return responses.Select(r => r.Subscription).OfType<Subscription>().ToList();
        }
        catch (SdkException ex)
        {
            throw Translate(nameof(_client.Customers.ListCustomerSubscriptions), ex);
        }
    }

    /// <returns>The subscription created with this reference, or null when Maxio has none.</returns>
    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        const string operation = nameof(_client.Subscriptions.FindSubscription);
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out RawError _))
            {
                throw FromProviderStatus(operation, ex.StatusCode, ex);
            }

            throw FromProviderStatus(operation, ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    // ---------------------------------------------------------------- error translation

    /// <summary>One mapping for every SDK failure; messages are caller-safe, provider detail goes to the log.</summary>
    private SubscriptionBillingException Translate(string operation, SdkException ex)
    {
        switch (ex)
        {
            case ResponseDeserializationException rde:
                _logger.LogError(rde, "Maxio {Operation} returned HTTP {StatusCode} with a body that did not match {TargetType}.",
                    operation, (int)rde.StatusCode, rde.TargetType.Name);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "The billing provider returned a response that could not be processed.", innerException: rde);
            case ApiException api:
                return FromProviderStatus(operation, api.StatusCode, api);
            case SdkTimeoutException timeout:
                _logger.LogWarning(timeout, "Maxio {Operation} did not answer within {Timeout}.", operation, timeout.Timeout);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.Unavailable,
                    "The billing provider did not respond in time. Please try again.", innerException: timeout);
            case SdkConnectionException connection:
                _logger.LogWarning(connection, "Maxio {Operation} could not be reached.", operation);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.Unavailable,
                    "The billing provider could not be reached. Please try again.", innerException: connection);
            case AuthSchemeException auth:
                _logger.LogError(auth, "Maxio credentials could not be applied for {Operation}.", operation);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "Subscription billing is temporarily unavailable.", innerException: auth);
            default:
                _logger.LogError(ex, "Maxio {Operation} failed.", operation);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "Subscription billing is temporarily unavailable.", innerException: ex);
        }
    }

    private SubscriptionBillingException FromProviderStatus(string operation, HttpStatusCode status, Exception ex)
    {
        var code = (int)status;
        switch (code)
        {
            case 401 or 403:
                // Our credentials, not the caller's: never pass an auth failure through as the caller's problem.
                _logger.LogError(ex, "Maxio {Operation} refused our credentials (HTTP {StatusCode}); check Maxio:ApiKey and Maxio:Subdomain.",
                    operation, code);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "Subscription billing is temporarily unavailable.", innerException: ex);
            case 429:
                _logger.LogWarning(ex, "Maxio {Operation} rate-limited us (HTTP 429).", operation);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.Unavailable,
                    "Subscription billing is busy. Please try again shortly.", innerException: ex);
            default:
                _logger.LogError(ex, "Maxio {Operation} failed with HTTP {StatusCode}.", operation, code);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "The billing provider could not complete the request.", innerException: ex);
        }
    }

    private static SubscriptionBillingException InProgress() =>
        new(SubscriptionBillingErrorKind.InProgress,
            "A subscription request for this account is already in progress. Check your subscriptions shortly.");

    private static SubscriptionBillingException BudgetExpired() =>
        new(SubscriptionBillingErrorKind.Unavailable, "The billing provider did not respond in time. Please try again.");

    // ---------------------------------------------------------------- helpers

    private CancellationTokenSource StartBudget(CancellationToken callerToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        budget.CancelAfter(RequestBudget);
        return budget;
    }

    /// <summary>
    /// Stable per user, so Maxio's one-customer-per-reference rule de-duplicates customer creation. Derived from the
    /// normalized user name (not the identity id, which a re-seeded in-memory store regenerates) and hashed so the
    /// reference, which travels in URLs and logs, carries no e-mail address.
    /// </summary>
    internal static string CustomerReference(Subscriber subscriber)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(subscriber.NormalizedUserName));
        return "eshop-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private static string NewSubscriptionReference() => $"eshop-sub-{Guid.NewGuid():N}";

    private static bool IsLive(Subscription subscription) =>
        subscription.State is null || !EndedStates.Contains(subscription.State);

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static IReadOnlyList<string> CustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is { } errors)
        {
            if (errors.TryGetCustomerError(out var customerError) && customerError.Customer is { } message)
            {
                return new[] { message };
            }

            if (errors.TryGetListOfString(out var list))
            {
                return list;
            }
        }

        return Array.Empty<string>();
    }

    private static SubscriptionDetails? ToDetails(Subscription subscription)
    {
        if (subscription.Id is not int id)
        {
            return null;
        }

        return new SubscriptionDetails(
            id,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents,
            subscription.Currency,
            subscription.State?.Value ?? "unknown",
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CreatedAt);
    }

    /// <summary>eShopOnWeb stores no personal names; Maxio requires them, so derive them from the e-mail local part.</summary>
    internal static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        var first = parts.Length > 0 ? textInfo.ToTitleCase(parts[0].ToLowerInvariant()) : "eShop";
        var last = parts.Length > 1 ? textInfo.ToTitleCase(string.Join(" ", parts.Skip(1)).ToLowerInvariant()) : "Customer";
        return (first, last);
    }
}
