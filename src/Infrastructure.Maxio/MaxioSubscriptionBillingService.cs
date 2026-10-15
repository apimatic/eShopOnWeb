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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing, the billing system of record.
/// Every write follows claim → SDK call → record; a write whose outcome is unknown is settled by
/// re-reading Maxio by the reference that was sent with it.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    internal const int PlanPageSize = 200;
    private const string DefaultLastName = "Customer";

    // States in which a subscription no longer counts as the buyer's subscription to a plan.
    private static readonly HashSet<string> EndedStates = new(StringComparer.Ordinal)
    {
        SubscriptionState.Canceled.Value,
        SubscriptionState.Expired.Value,
        SubscriptionState.FailedToCreate.Value,
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly ISubscriptionClaimStore _claims;
    private readonly IMemoryCache _cache;
    private readonly MaxioSettings _settings;
    private readonly CollectionMethod _collectionMethod;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        ISubscriptionClaimStore claims,
        IMemoryCache cache,
        IOptions<MaxioSettings> settings,
        TimeProvider time,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _claims = claims;
        _cache = cache;
        _settings = settings.Value;
        _collectionMethod = CollectionMethod.TryGetKnownValue(_settings.PaymentCollectionMethod?.Trim().ToLowerInvariant(), out var method)
            ? method
            : throw new InvalidOperationException("Maxio:PaymentCollectionMethod is not a supported collection method.");
        _time = time;
        _logger = logger;
    }

    private string PlanCacheKey => "maxio:plans:" + _settings.ProductFamilyHandle;

    // ---------------------------------------------------------------- plans

    public async Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(PlanCacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var catalog = await Bounded(FetchPlansAsync, cancellationToken);
        if (_settings.PlanCacheSeconds > 0)
        {
            _cache.Set(PlanCacheKey, catalog, _settings.PlanCacheDuration);
        }
        return catalog;
    }

    private async Task<SubscriptionPlanCatalog> FetchPlansAsync(CancellationToken ct)
    {
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
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PlanPageSize,
                        IncludeArchived = false,
                    },
                    cancellationToken: ct);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    _logger.LogError(ex, "Maxio product family configured in Maxio:ProductFamilyHandle was not found (HTTP {Status}).", (int)ex.StatusCode);
                    throw new BillingException(BillingErrorKind.ProviderError, "Subscription plans are not available right now.", (int)ex.StatusCode, ex);
                }
                throw Translate(ex, "ListProductsForProductFamily");
            }
            catch (SdkException ex)
            {
                throw Translate(ex, "ListProductsForProductFamily");
            }

            foreach (var item in batch)
            {
                var plan = ToPlan(item.Product);
                if (plan is not null)
                {
                    plans.Add(plan);
                }
            }

            if (batch.Count < PlanPageSize)
            {
                break;
            }
            if (page >= _settings.MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Maxio plan listing stopped at the {MaxPages}-page cap; the plan catalog is truncated.", _settings.MaxPlanPages);
                break;
            }
        }

        return new SubscriptionPlanCatalog(plans, truncated);
    }

    private static SubscriptionPlan? ToPlan(Product? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
        {
            return null;
        }

        return new SubscriptionPlan(
            product.Handle,
            product.Name ?? product.Handle,
            product.Description,
            product.PriceInCents ?? 0,
            product.Interval,
            product.IntervalUnit?.Value,
            product.TrialInterval,
            product.TrialIntervalUnit?.Value,
            product.RequireCreditCard ?? false);
    }

    /// <summary>The plan the caller asked for, if — and only if — the configured family offers it.</summary>
    private async Task<SubscriptionPlan> GetOfferedPlanAsync(string planHandle, CancellationToken ct)
    {
        var catalog = await ListPlansAsync(ct);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, $"'{planHandle}' is not an available subscription plan.");
        }
        if (plan.RequiresPaymentMethod)
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, $"The '{planHandle}' plan requires a payment method, which this checkout does not collect.");
        }
        return plan;
    }

    // ---------------------------------------------------------------- subscribe

    public Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.BuyerId))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "The caller is not identified.");
        }
        if (string.IsNullOrWhiteSpace(command.PlanHandle))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "A plan handle is required.");
        }
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "An e-mail address is required to subscribe.");
        }

        return Bounded(ct => SubscribeCoreAsync(command, ct), cancellationToken);
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(SubscribeCommand command, CancellationToken ct)
    {
        var plan = await GetOfferedPlanAsync(command.PlanHandle.Trim(), ct);

        // Claim first: a second request for the same buyer + plan is refused by the primary key
        // before it can reach Maxio.
        var enrollment = new SubscriptionEnrollment(command.BuyerId, plan.Handle, NewSubscriptionReference(), _time.GetUtcNow());
        var existing = await _claims.TryClaimEnrollmentAsync(enrollment, ct);
        if (existing is not null)
        {
            var (replay, reclaimed) = await ResolveExistingEnrollmentAsync(existing, command, plan, ct);
            if (replay is not null)
            {
                return replay;
            }
            enrollment = reclaimed!;
        }

        int customerId;
        SubscriptionSummary? live;
        try
        {
            customerId = await EnsureCustomerAsync(command, ct);
            // Maxio is the system of record: if it already holds a live subscription to this plan for this
            // customer (e.g. our claim row was lost), adopt it instead of creating a second one.
            live = await FindLiveSubscriptionForPlanAsync(customerId, plan.Handle, ct);
        }
        catch
        {
            // Nothing was sent to the subscription endpoint yet: release the claim so the buyer can retry.
            await _claims.ReleaseEnrollmentAsync(enrollment, CancellationToken.None);
            throw;
        }

        if (live is not null)
        {
            enrollment.MarkActive(live.SubscriptionId, _time.GetUtcNow());
            await _claims.TrySaveEnrollmentAsync(enrollment, CancellationToken.None);
            _logger.LogInformation("Adopted existing Maxio subscription {SubscriptionId} for plan {PlanHandle}.", live.SubscriptionId, plan.Handle);
            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, live, live.Reference ?? enrollment.SubscriptionReference);
        }

        return await CreateSubscriptionAsync(enrollment, customerId, plan, ct);
    }

    /// <summary>
    /// Handles a refused claim. Returns either a result to replay, or the enrollment row re-claimed
    /// for a fresh attempt.
    /// </summary>
    private async Task<(SubscribeResult? Replay, SubscriptionEnrollment? Reclaimed)> ResolveExistingEnrollmentAsync(
        SubscriptionEnrollment existing, SubscribeCommand command, SubscriptionPlan plan, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        if (existing.IsClaimFresh(now, _settings.ClaimLease))
        {
            throw new BillingException(BillingErrorKind.Conflict, "A subscription request for this plan is already in progress.");
        }

        if (existing.Status == EnrollmentStatus.Active && existing.BillingSubscriptionId is int activeId)
        {
            var customerId = await FindCustomerIdAsync(command.BuyerId, ct);
            if (customerId is int id)
            {
                var current = (await ListCustomerSubscriptionsAsync(id, ct)).FirstOrDefault(s => s.SubscriptionId == activeId);
                if (current is not null && !EndedStates.Contains(current.State))
                {
                    return (new SubscribeResult(SubscribeOutcome.AlreadySubscribed, current, existing.SubscriptionReference), null);
                }
            }
            // The recorded subscription has ended (or is gone): the buyer may subscribe again.
        }
        else
        {
            // Pending past its lease, or Unknown: settle the earlier attempt by its reference first.
            var settled = await LookupSubscriptionByReferenceAsync(existing.SubscriptionReference, ct);
            if (settled.Found is { } found && !EndedStates.Contains(found.State))
            {
                existing.MarkActive(found.SubscriptionId, now);
                if (!await _claims.TrySaveEnrollmentAsync(existing, ct))
                {
                    throw new BillingException(BillingErrorKind.Conflict, "A subscription request for this plan is already in progress.");
                }
                return (new SubscribeResult(SubscribeOutcome.AlreadySubscribed, found, existing.SubscriptionReference), null);
            }
            if (!settled.Known)
            {
                throw new BillingException(BillingErrorKind.Unavailable, "The outcome of an earlier subscription request could not be confirmed yet. Please try again shortly.");
            }
        }

        // Proven not to hold a live subscription: take the row over for a new attempt. The concurrency
        // stamp makes this a claim too — only one concurrent request can win it.
        existing.Reclaim(NewSubscriptionReference(), now);
        if (!await _claims.TrySaveEnrollmentAsync(existing, ct))
        {
            throw new BillingException(BillingErrorKind.Conflict, "A subscription request for this plan is already in progress.");
        }
        _logger.LogInformation("Re-claimed enrollment for plan {PlanHandle} with new reference {SubscriptionReference}.", plan.Handle, existing.SubscriptionReference);
        return (null, existing);
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(SubscriptionEnrollment enrollment, int customerId, SubscriptionPlan plan, CancellationToken ct)
    {
        SubscriptionResponse response;
        try
        {
            // POST: never resent by the SDK (default retry methods exclude it).
            response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = enrollment.SubscriptionReference,
                            PaymentCollectionMethod = _collectionMethod,
                        },
                    },
                },
                cancellationToken: ct);
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode < 500)
        {
            await _claims.ReleaseEnrollmentAsync(enrollment, CancellationToken.None);
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var messages = string.Join("; ", errorList.Errors);
                _logger.LogWarning(ex, "Maxio rejected subscription {SubscriptionReference} for plan {PlanHandle} (HTTP {Status}): {Errors}",
                    enrollment.SubscriptionReference, plan.Handle, (int)ex.StatusCode, messages);
                throw new BillingException(BillingErrorKind.Rejected, "The subscription was rejected by the billing system: " + messages, (int)ex.StatusCode, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("Maxio CreateSubscription error body (HTTP {Status}): {Body}", (int)raw.StatusCode, Truncate(raw.ReadAsString()));
            }
            throw Translate(ex, "CreateSubscription");
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            // Rejected; only the error detail was unreadable.
            await _claims.ReleaseEnrollmentAsync(enrollment, CancellationToken.None);
            throw Translate(ex, "CreateSubscription");
        }
        catch (AuthSchemeException ex)
        {
            // The credential could not be applied, so nothing was sent.
            await _claims.ReleaseEnrollmentAsync(enrollment, CancellationToken.None);
            throw Translate(ex, "CreateSubscription");
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            // Transport failure, timeout, budget/caller cancellation, a 5xx, or an unreadable 2xx:
            // the subscription may exist. Settle by the reference we sent.
            _logger.LogError(ex, "Outcome of Maxio subscription {SubscriptionReference} is unknown; settling by reference.", enrollment.SubscriptionReference);
            return await SettleCreateSubscriptionAsync(enrollment, plan);
        }

        if (response.Subscription?.Id is null)
        {
            _logger.LogError("Maxio accepted subscription {SubscriptionReference} but returned no subscription; settling by reference.", enrollment.SubscriptionReference);
            return await SettleCreateSubscriptionAsync(enrollment, plan);
        }

        var summary = ToSummary(response.Subscription)!;
        enrollment.MarkActive(summary.SubscriptionId, _time.GetUtcNow());
        await _claims.TrySaveEnrollmentAsync(enrollment, CancellationToken.None);
        _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({SubscriptionReference}) for customer {CustomerId} on plan {PlanHandle}; state {State}.",
            summary.SubscriptionId, enrollment.SubscriptionReference, customerId, plan.Handle, summary.State);
        return new SubscribeResult(SubscribeOutcome.Created, summary, enrollment.SubscriptionReference);
    }

    /// <summary>
    /// Settles a CreateSubscription whose outcome is unknown, on its own budget (the caller may be gone).
    /// Found → recorded as active. Proven absent → claim released, caller told to retry. Still unknown →
    /// recorded as <see cref="EnrollmentStatus.Unknown"/>, settled later by <see cref="SettleOpenEnrollmentsAsync"/>
    /// or the next subscribe attempt.
    /// </summary>
    private async Task<SubscribeResult> SettleCreateSubscriptionAsync(SubscriptionEnrollment enrollment, SubscriptionPlan plan)
    {
        using var settlement = new CancellationTokenSource(_settings.SettlementBudget);
        var lookup = await LookupSubscriptionByReferenceAsync(enrollment.SubscriptionReference, settlement.Token, throwOnFailure: false);

        if (lookup.Found is { } found)
        {
            enrollment.MarkActive(found.SubscriptionId, _time.GetUtcNow());
            await _claims.TrySaveEnrollmentAsync(enrollment, CancellationToken.None);
            _logger.LogInformation("Settled Maxio subscription {SubscriptionReference}: created as {SubscriptionId}.", enrollment.SubscriptionReference, found.SubscriptionId);
            return new SubscribeResult(SubscribeOutcome.Created, found, enrollment.SubscriptionReference);
        }

        if (lookup.Known)
        {
            await _claims.ReleaseEnrollmentAsync(enrollment, CancellationToken.None);
            _logger.LogWarning("Settled Maxio subscription {SubscriptionReference}: not created.", enrollment.SubscriptionReference);
            throw new BillingException(BillingErrorKind.Unavailable, "The billing system could not complete the subscription; nothing was created. Please try again.");
        }

        enrollment.MarkUnknown();
        await _claims.TrySaveEnrollmentAsync(enrollment, CancellationToken.None);
        _logger.LogError("Maxio subscription {SubscriptionReference} for plan {PlanHandle} remains unconfirmed; recorded for later settlement.", enrollment.SubscriptionReference, plan.Handle);
        return new SubscribeResult(SubscribeOutcome.PendingConfirmation, null, enrollment.SubscriptionReference);
    }

    /// <param name="Known">True when Maxio answered definitively (found, or 404 not found).</param>
    private readonly record struct SubscriptionLookup(bool Known, SubscriptionSummary? Found);

    private async Task<SubscriptionLookup> LookupSubscriptionByReferenceAsync(string reference, CancellationToken ct, bool throwOnFailure = true)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            var summary = ToSummary(response.Subscription);
            return summary is null ? new SubscriptionLookup(false, null) : new SubscriptionLookup(true, summary);
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return new SubscriptionLookup(true, null);
        }
        catch (Exception ex) when (!throwOnFailure && ex is SdkException or OperationCanceledException)
        {
            _logger.LogError(ex, "Could not look up Maxio subscription {SubscriptionReference}.", reference);
            return new SubscriptionLookup(false, null);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "FindSubscription");
        }
    }

    // ---------------------------------------------------------------- customers

    /// <summary>
    /// The Maxio customer id for this buyer, creating the customer if needed. The id only ever comes from
    /// Maxio's answer for this buyer's own deterministic reference.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(SubscribeCommand command, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var local = await _claims.GetCustomerAsync(command.BuyerId, ct);
        if (local?.BillingCustomerId is int linked)
        {
            return linked;
        }

        // Claim first: a concurrent second insert for the same buyer is refused by the primary key.
        BillingCustomer claim;
        bool heldByAnotherRequest;
        if (local is null)
        {
            claim = new BillingCustomer(command.BuyerId, CustomerReferenceFor(command.BuyerId), now);
            var other = await _claims.TryClaimCustomerAsync(claim, ct);
            if (other?.BillingCustomerId is int otherId)
            {
                return otherId;
            }
            heldByAnotherRequest = other is not null && other.IsClaimFresh(now, _settings.ClaimLease);
            claim = other ?? claim;
        }
        else
        {
            // An unlinked row: either another request is creating the customer now, or an earlier
            // attempt ended with an unknown outcome (stale claim, taken over below).
            claim = local;
            heldByAnotherRequest = local.IsClaimFresh(now, _settings.ClaimLease);
        }

        // The customer may already exist at Maxio (an earlier attempt whose outcome was unknown, or a lost
        // local record). The reference is unique per site, so look it up before creating.
        var existingId = await FindCustomerIdByReferenceAsync(claim.CustomerReference, ct);
        if (existingId is int found)
        {
            await LinkCustomerAsync(claim, found);
            return found;
        }

        if (heldByAnotherRequest)
        {
            throw new BillingException(BillingErrorKind.Conflict, "A billing account for this user is being set up. Please try again shortly.");
        }

        return await CreateCustomerAsync(claim, command, ct);
    }

    private async Task<int> CreateCustomerAsync(BillingCustomer claim, SubscribeCommand command, CancellationToken ct)
    {
        var email = command.Email.Trim();
        var firstName = string.IsNullOrWhiteSpace(command.FirstName) ? DeriveFirstName(email) : command.FirstName.Trim();
        var lastName = string.IsNullOrWhiteSpace(command.LastName) ? DefaultLastName : command.LastName.Trim();

        CustomerResponse response;
        try
        {
            // POST: never resent by the SDK. The reference is the real idempotency key (unique per Maxio site).
            response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = claim.CustomerReference,
                        },
                    },
                },
                cancellationToken: ct);
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode < 500)
        {
            // A 422 can mean the reference is already taken by a customer an earlier attempt created.
            var again = await FindCustomerIdByReferenceAsync(claim.CustomerReference, ct);
            if (again is int existingId)
            {
                await LinkCustomerAsync(claim, existingId);
                return existingId;
            }

            await _claims.ReleaseCustomerAsync(claim, CancellationToken.None);
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                var messages = DescribeCustomerErrors(body);
                _logger.LogWarning(ex, "Maxio rejected customer {CustomerReference} (HTTP {Status}): {Errors}", claim.CustomerReference, (int)ex.StatusCode, messages);
                throw new BillingException(BillingErrorKind.Rejected, "The billing account could not be created: " + messages, (int)ex.StatusCode, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("Maxio CreateCustomer error body (HTTP {Status}): {Body}", (int)raw.StatusCode, Truncate(raw.ReadAsString()));
            }
            throw Translate(ex, "CreateCustomer");
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            await _claims.ReleaseCustomerAsync(claim, CancellationToken.None);
            throw Translate(ex, "CreateCustomer");
        }
        catch (AuthSchemeException ex)
        {
            await _claims.ReleaseCustomerAsync(claim, CancellationToken.None);
            throw Translate(ex, "CreateCustomer");
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            _logger.LogError(ex, "Outcome of Maxio customer creation {CustomerReference} is unknown; settling by reference.", claim.CustomerReference);
            return await SettleCreateCustomerAsync(claim);
        }

        if (response.Customer.Id is not int customerId)
        {
            return await SettleCreateCustomerAsync(claim);
        }

        await LinkCustomerAsync(claim, customerId);
        _logger.LogInformation("Created Maxio customer {CustomerId} ({CustomerReference}).", customerId, claim.CustomerReference);
        return customerId;
    }

    /// <summary>
    /// Settles a CreateCustomer whose outcome is unknown, on its own budget. Found → linked. Proven absent →
    /// claim released. Still unknown → the unlinked claim stays; the next attempt re-reads by reference
    /// (and a duplicate create is refused by Maxio's unique reference).
    /// </summary>
    private async Task<int> SettleCreateCustomerAsync(BillingCustomer claim)
    {
        using var settlement = new CancellationTokenSource(_settings.SettlementBudget);
        int? found;
        try
        {
            found = await FindCustomerIdByReferenceAsync(claim.CustomerReference, settlement.Token);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            _logger.LogError(ex, "Maxio customer {CustomerReference} remains unconfirmed; it will be re-read on the next attempt.", claim.CustomerReference);
            throw new BillingException(BillingErrorKind.Unavailable, "The billing system did not respond in time. Please try again shortly.");
        }

        if (found is int customerId)
        {
            await LinkCustomerAsync(claim, customerId);
            _logger.LogInformation("Settled Maxio customer {CustomerReference}: created as {CustomerId}.", claim.CustomerReference, customerId);
            return customerId;
        }

        await _claims.ReleaseCustomerAsync(claim, CancellationToken.None);
        throw new BillingException(BillingErrorKind.Unavailable, "The billing system could not be reached; nothing was created. Please try again.");
    }

    private async Task LinkCustomerAsync(BillingCustomer claim, int customerId)
    {
        claim.Link(customerId, _time.GetUtcNow());
        await _claims.SaveCustomerAsync(claim, CancellationToken.None);
    }

    /// <summary>The buyer's Maxio customer id, or null when the buyer has none (read-only; creates nothing).</summary>
    private async Task<int?> FindCustomerIdAsync(string buyerId, CancellationToken ct)
    {
        var local = await _claims.GetCustomerAsync(buyerId, ct);
        if (local?.BillingCustomerId is int linked)
        {
            return linked;
        }

        var found = await FindCustomerIdByReferenceAsync(local?.CustomerReference ?? CustomerReferenceFor(buyerId), ct);
        if (found is int id && local is not null)
        {
            await LinkCustomerAsync(local, id);
        }
        return found;
    }

    private async Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            if (response.Customer.Id is int id)
            {
                return id;
            }
            throw new BillingException(BillingErrorKind.ProviderError, "The billing system returned an incomplete customer record.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "ReadCustomerByReference");
        }
    }

    // ---------------------------------------------------------------- my subscriptions

    public Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "The caller is not identified.");
        }

        return Bounded(async ct =>
        {
            await SettleOpenEnrollmentsAsync(buyerId, ct);

            var customerId = await FindCustomerIdAsync(buyerId, ct);
            if (customerId is not int id)
            {
                return (IReadOnlyList<SubscriptionSummary>)Array.Empty<SubscriptionSummary>();
            }

            return await ListCustomerSubscriptionsAsync(id, ct);
        }, cancellationToken);
    }

    /// <summary>
    /// The sweep for writes whose outcome was unknown: re-reads each of the buyer's Unknown or stale
    /// Pending enrollments by reference and records what Maxio holds. Best effort; never fails the read.
    /// </summary>
    private async Task SettleOpenEnrollmentsAsync(string buyerId, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var open = (await _claims.ListEnrollmentsAsync(buyerId, ct))
            .Where(e => e.Status == EnrollmentStatus.Unknown
                        || (e.Status == EnrollmentStatus.Pending && !e.IsClaimFresh(now, _settings.ClaimLease)))
            .ToList();

        foreach (var enrollment in open)
        {
            var lookup = await LookupSubscriptionByReferenceAsync(enrollment.SubscriptionReference, ct, throwOnFailure: false);
            if (lookup.Found is { } found)
            {
                enrollment.MarkActive(found.SubscriptionId, _time.GetUtcNow());
                await _claims.TrySaveEnrollmentAsync(enrollment, ct);
                _logger.LogInformation("Settled Maxio subscription {SubscriptionReference}: created as {SubscriptionId}.", enrollment.SubscriptionReference, found.SubscriptionId);
            }
            else if (lookup.Known)
            {
                await _claims.ReleaseEnrollmentAsync(enrollment, ct);
                _logger.LogWarning("Settled Maxio subscription {SubscriptionReference}: not created; claim released.", enrollment.SubscriptionReference);
            }
        }
    }

    private async Task<SubscriptionSummary?> FindLiveSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken ct) =>
        (await ListCustomerSubscriptionsAsync(customerId, ct))
            .Where(s => string.Equals(s.PlanHandle, planHandle, StringComparison.Ordinal) && !EndedStates.Contains(s.State))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();

    private async Task<IReadOnlyList<SubscriptionSummary>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "ListCustomerSubscriptions");
        }

        return responses
            .Select(r => ToSummary(r.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    // ---------------------------------------------------------------- mapping & helpers

    internal static SubscriptionSummary? ToSummary(Subscription? subscription)
    {
        if (subscription?.Id is not int id)
        {
            return null;
        }

        return new SubscriptionSummary(
            id,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            subscription.Currency,
            subscription.Product?.Interval,
            subscription.Product?.IntervalUnit?.Value,
            subscription.State?.Value ?? "unknown",
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodEndsAt,
            subscription.ActivatedAt,
            subscription.CreatedAt,
            subscription.Reference);
    }

    /// <summary>
    /// Deterministic, non-personal Maxio customer reference for a buyer: stable across restarts and
    /// hosts, so a lost local record can always be re-found, and no e-mail address ends up in URLs or logs.
    /// </summary>
    internal static string CustomerReferenceFor(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId.Trim().ToLowerInvariant()));
        return "eshop-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";

    private static string NewSubscriptionReference() => "eshop-sub-" + Guid.NewGuid().ToString("N");

    private static string DeriveFirstName(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(local) ? "eShop" : local;
    }

    private static string DescribeCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is { } errors)
        {
            if (errors.TryGetListOfString(out var list) && list.Count > 0)
            {
                return string.Join("; ", list);
            }
            if (errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
            {
                return customerError.Customer;
            }
        }
        return "validation failed";
    }

    /// <summary>One budget for a whole operation, linked to the caller's cancellation.</summary>
    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_settings.OperationBudget);
        try
        {
            return await work(budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Billing operation exceeded its {Budget}s budget.", _settings.OperationBudgetSeconds);
            throw new BillingException(BillingErrorKind.Unavailable, "The billing system did not respond in time. Please try again.", null, ex);
        }
    }

    /// <summary>
    /// The single translation from SDK failures to <see cref="BillingException"/>. Typed (Case A) bodies are
    /// read at each call site first; this handles status, transport and parse failures uniformly.
    /// </summary>
    internal BillingException Translate(SdkException exception, string operation)
    {
        switch (exception)
        {
            case ResponseDeserializationException ex:
                _logger.LogError(ex, "Maxio {Operation} returned an unreadable {TargetType} body (HTTP {Status}).", operation, ex.TargetType.Name, (int)ex.StatusCode);
                return (int)ex.StatusCode is >= 400 and < 500
                    ? new BillingException(BillingErrorKind.Rejected, "The billing system rejected the request.", (int)ex.StatusCode, ex)
                    : new BillingException(BillingErrorKind.ProviderError, "The billing system returned a response that could not be processed.", (int)ex.StatusCode, ex);

            case ApiException ex:
                var status = (int)ex.StatusCode;
                if (status is 401 or 403)
                {
                    _logger.LogError(ex, "Maxio {Operation} refused our credentials (HTTP {Status}); check Maxio:ApiKey and Maxio:Subdomain.", operation, status);
                    return new BillingException(BillingErrorKind.Unavailable, "The billing system is unavailable.", status, ex);
                }
                if (status == 404)
                {
                    // Every expected 404 (unknown reference) is handled at its call site; any other points at
                    // configuration (site, family) or a resource removed at Maxio — not the caller's fault.
                    _logger.LogError(ex, "Maxio {Operation} returned an unexpected 404; check Maxio:Subdomain / Maxio:BaseUrl.", operation);
                    return new BillingException(BillingErrorKind.ProviderError, "The billing system could not find a required record.", status, ex);
                }
                if (status == 429)
                {
                    _logger.LogWarning(ex, "Maxio {Operation} throttled us (HTTP 429).", operation);
                    return new BillingException(BillingErrorKind.Unavailable, "The billing system is busy. Please try again shortly.", status, ex);
                }
                if (status is >= 400 and < 500)
                {
                    _logger.LogWarning(ex, "Maxio {Operation} rejected the request (HTTP {Status}).", operation, status);
                    return new BillingException(BillingErrorKind.Rejected, "The billing system rejected the request.", status, ex);
                }
                _logger.LogError(ex, "Maxio {Operation} failed (HTTP {Status}).", operation, status);
                return new BillingException(BillingErrorKind.Unavailable, "The billing system is unavailable. Please try again.", status, ex);

            case SdkTimeoutException ex:
                _logger.LogError(ex, "Maxio {Operation} timed out after {Timeout}.", operation, ex.Timeout);
                return new BillingException(BillingErrorKind.Unavailable, "The billing system did not respond in time. Please try again.", null, ex);

            case SdkConnectionException ex:
                _logger.LogError(ex, "Maxio {Operation} could not be reached.", operation);
                return new BillingException(BillingErrorKind.Unavailable, "The billing system could not be reached. Please try again.", null, ex);

            case AuthSchemeException ex:
                _logger.LogError(ex, "Maxio {Operation}: credentials could not be applied.", operation);
                return new BillingException(BillingErrorKind.Unavailable, "The billing system is unavailable.", null, ex);

            default:
                _logger.LogError(exception, "Maxio {Operation} failed.", operation);
                return new BillingException(BillingErrorKind.ProviderError, "The billing request failed.", null, exception);
        }
    }
}
