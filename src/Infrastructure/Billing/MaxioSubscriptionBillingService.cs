using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscription billing with Maxio Advanced Billing as the system of record. The only class that talks to
/// the Maxio SDK: it owns the per-request call budget, the translation of SDK failures into
/// <see cref="BillingProviderException"/>, duplicate prevention for writes and the settling of writes whose
/// outcome is unknown.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // The provider's maximum page size for product lists.
    private const int PlanPageSize = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly BillingClaimStore _claims;
    private readonly MaxioSiteSettingsCache _siteSettings;
    private readonly MaxioOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(MaxioAdvancedBillingClient client, BillingClaimStore claims,
        MaxioSiteSettingsCache siteSettings, IOptions<MaxioOptions> options, TimeProvider clock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _claims = claims;
        _siteSettings = siteSettings;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default) =>
        BoundedAsync(LoadPlansAsync, cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(BillingBuyer buyer, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(buyer, nameof(buyer));
        Guard.Against.NullOrWhiteSpace(buyer.UserName, nameof(buyer.UserName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        return BoundedAsync(ct => SubscribeCoreAsync(buyer, planHandle.Trim(), ct), cancellationToken);
    }

    public Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(BillingBuyer buyer, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(buyer, nameof(buyer));
        Guard.Against.NullOrWhiteSpace(buyer.UserName, nameof(buyer.UserName));
        return BoundedAsync(ct => ListSubscriptionsCoreAsync(buyer, ct), cancellationToken);
    }

    // ---- Plans -------------------------------------------------------------------------------------------

    private async Task<SubscriptionPlanCatalog> LoadPlansAsync(CancellationToken ct)
    {
        var familyId = "handle:" + _options.ProductFamilyHandle!.Trim();
        var plans = new List<SubscriptionPlan>();
        var lastPageWasFull = false;

        for (var page = 1; page <= _options.MaxPlanPages; page++)
        {
            var currentPage = page;
            var batch = await CallAsync("ListProductsForProductFamily", async t =>
            {
                try
                {
                    return await _client.ProductFamilies.ListProductsForProductFamily(
                        new ListProductsForProductFamilyRequest { ProductFamilyId = familyId, Page = currentPage, PerPage = PlanPageSize },
                        cancellationToken: t);
                }
                catch (ApiException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out var message))
                    {
                        // 404: the configured family does not exist on this site — a deployment fault, not the caller's.
                        _logger.LogError("Maxio product family {ProductFamilyHandle} was not found (HTTP {StatusCode}): {Message}",
                            _options.ProductFamilyHandle, (int)ex.StatusCode, message);
                        throw new BillingProviderException("Subscription plans are not available.",
                            BillingFailureKind.ProviderError, (int)ex.StatusCode, innerException: ex);
                    }
                    if (ex.Error.TryGetRawError(out _))
                    {
                        throw FromStatus("ListProductsForProductFamily", ex.StatusCode, ex);
                    }
                    throw FromStatus("ListProductsForProductFamily", ex.StatusCode, ex);
                }
            }, ct);

            foreach (var product in batch.Select(r => r?.Product))
            {
                if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }
                plans.Add(new SubscriptionPlan(product.Handle, product.Name ?? product.Handle, product.Description,
                    product.PriceInCents, product.Interval, product.IntervalUnit?.Value, product.RequireCreditCard));
            }

            lastPageWasFull = batch.Count >= PlanPageSize;
            if (!lastPageWasFull)
            {
                break;
            }
        }

        if (lastPageWasFull)
        {
            _logger.LogWarning("Plan list for product family {ProductFamilyHandle} stopped at the {MaxPages}-page cap; result is partial.",
                _options.ProductFamilyHandle, _options.MaxPlanPages);
        }

        return new SubscriptionPlanCatalog(plans, IsTruncated: lastPageWasFull);
    }

    // ---- Subscribe ---------------------------------------------------------------------------------------

    private async Task<SubscribeResult> SubscribeCoreAsync(BillingBuyer buyer, string planHandle, CancellationToken ct)
    {
        // Only a plan this application offers may be subscribed to: check against the family's list, not the site.
        var catalog = await LoadPlansAsync(ct);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (catalog.IsTruncated)
            {
                throw new BillingProviderException("Subscription plans could not be fully read; try again later.",
                    BillingFailureKind.Unavailable);
            }
            throw new SubscriptionPlanNotOfferedException(planHandle);
        }

        var reference = BillingReferences.ForSubscription(buyer.UserName, plan.Handle);
        var collectionMethod = await ResolveCollectionMethodAsync(plan, ct);

        var customerId = await EnsureCustomerAsync(buyer, ct);
        if (customerId is null)
        {
            return new SubscribeResult(SubscribeStatus.Pending, reference, null);
        }

        var (acquired, claim) = await _claims.TryAcquireAsync(reference, BillingClaimKind.Subscription, buyer.UserName, plan.Handle, ct);
        if (!acquired)
        {
            if (claim.State == BillingClaimState.Completed && claim.ProviderSubscriptionId is int existingId)
            {
                var current = await ReadSubscriptionAsync(existingId, ct);
                return new SubscribeResult(SubscribeStatus.AlreadySubscribed, reference, ToSummary(current, plan.Handle, plan.Name));
            }
            if (!await TryTakeOverAsync(claim, ct))
            {
                _logger.LogInformation("Subscription {Reference} is being created by another request.", reference);
                return new SubscribeResult(SubscribeStatus.Pending, reference, null);
            }
        }

        // This request owns the claim. Look before writing: the subscription may already exist from an earlier
        // run or from an attempt whose outcome was never settled.
        Subscription? existing;
        try
        {
            existing = await FindSubscriptionAsync(reference, ct);
        }
        catch
        {
            await _claims.ReleaseAsync(claim);
            throw;
        }

        if (existing?.Id is int foundId)
        {
            await _claims.CompleteSubscriptionAsync(claim, customerId.Value, foundId);
            return new SubscribeResult(SubscribeStatus.AlreadySubscribed, reference, ToSummary(existing, plan.Handle, plan.Name));
        }

        return await CreateSubscriptionAsync(customerId.Value, plan, collectionMethod, reference, claim, ct);
    }

    /// <summary>
    /// A plan that needs no payment method is billed by invoice rather than by charging a card on file — otherwise
    /// Maxio refuses the signup for want of a payment method. The invoice collection method's name depends on the
    /// site's invoicing architecture: <c>remittance</c> under Relationship Invoicing, <c>invoice</c> under the legacy
    /// Statements architecture. Plans that require a card keep Maxio's default (automatic) collection.
    /// </summary>
    private async Task<CollectionMethod?> ResolveCollectionMethodAsync(SubscriptionPlan plan, CancellationToken ct)
    {
        if (plan.RequiresCreditCard == true)
        {
            return null;
        }

        var relationshipInvoicing = await _siteSettings.GetRelationshipInvoicingEnabledAsync(
            token => CallAsync("ReadSite", async t =>
            {
                var response = await _client.Sites.ReadSite(cancellationToken: t);
                return response.Site?.RelationshipInvoicingEnabled ?? throw Unreadable("ReadSite");
            }, token), ct);

        return relationshipInvoicing ? CollectionMethod.Remittance : CollectionMethod.Invoice;
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(int customerId, SubscriptionPlan plan,
        CollectionMethod? collectionMethod, string reference, BillingClaim claim, CancellationToken ct)
    {
        var request = new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customerId,
                    Reference = reference,
                    PaymentCollectionMethod = collectionMethod
                }
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(request, cancellationToken: ct);
            if (response.Subscription?.Id is int subscriptionId)
            {
                await _claims.CompleteSubscriptionAsync(claim, customerId, subscriptionId);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({Reference}) on plan {PlanHandle}.",
                    subscriptionId, reference, plan.Handle);
                return new SubscribeResult(SubscribeStatus.Created, reference, ToSummary(response.Subscription, plan.Handle, plan.Name));
            }

            _logger.LogWarning("CreateSubscription for {Reference} succeeded without a subscription id; settling by lookup.", reference);
            return await SettleSubscriptionAsync(customerId, plan, reference, claim);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var rejection))
            {
                await _claims.ReleaseAsync(claim);
                _logger.LogWarning("Maxio rejected subscription {Reference} (HTTP {StatusCode}): {Errors}",
                    reference, (int)ex.StatusCode, string.Join("; ", rejection.Errors));
                throw new BillingProviderException("The subscription could not be created.", BillingFailureKind.Rejected,
                    (int)ex.StatusCode, rejection.Errors, ex);
            }
            if (IsServerError(ex.StatusCode))
            {
                // A provider failure on a write does not prove the write did not land.
                _logger.LogWarning(ex, "CreateSubscription for {Reference} failed with HTTP {StatusCode}; settling by lookup.",
                    reference, (int)ex.StatusCode);
                return await SettleSubscriptionAsync(customerId, plan, reference, claim);
            }
            if (ex.Error.TryGetRawError(out _))
            {
                await _claims.ReleaseAsync(claim);
                throw FromStatus("CreateSubscription", ex.StatusCode, ex);
            }
            await _claims.ReleaseAsync(claim);
            throw FromStatus("CreateSubscription", ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex) when (IsSuccess(ex.StatusCode) || IsServerError(ex.StatusCode))
        {
            _logger.LogWarning(ex, "CreateSubscription for {Reference} returned an unreadable HTTP {StatusCode} body; settling by lookup.",
                reference, (int)ex.StatusCode);
            return await SettleSubscriptionAsync(customerId, plan, reference, claim);
        }
        catch (ResponseDeserializationException ex)
        {
            await _claims.ReleaseAsync(claim);
            throw FromStatus("CreateSubscription", ex.StatusCode, ex);
        }
        catch (ApiException ex) when (IsServerError(ex.StatusCode))
        {
            _logger.LogWarning(ex, "CreateSubscription for {Reference} failed with HTTP {StatusCode}; settling by lookup.",
                reference, (int)ex.StatusCode);
            return await SettleSubscriptionAsync(customerId, plan, reference, claim);
        }
        catch (ApiException ex)
        {
            await _claims.ReleaseAsync(claim);
            throw FromStatus("CreateSubscription", ex.StatusCode, ex);
        }
        catch (SdkConnectionException ex)
        {
            // Includes SdkTimeoutException: the request may have reached Maxio before the connection failed.
            _logger.LogWarning(ex, "CreateSubscription for {Reference} lost its connection; settling by lookup.", reference);
            return await SettleSubscriptionAsync(customerId, plan, reference, claim);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("CreateSubscription for {Reference} was cancelled in flight; settling by lookup.", reference);
            return await SettleSubscriptionAsync(customerId, plan, reference, claim);
        }
        catch (AuthSchemeException ex)
        {
            // The credential could not be applied, so nothing was sent.
            await _claims.ReleaseAsync(claim);
            _logger.LogError(ex, "Maxio credentials could not be applied.");
            throw new BillingProviderException("Billing is temporarily unavailable.", BillingFailureKind.Unavailable, innerException: ex);
        }
    }

    /// <summary>
    /// Settles a CreateSubscription whose outcome is unknown by looking the subscription up by its reference.
    /// Found → recorded as created. Not found or unreadable → the claim is marked Unknown and the caller is told
    /// the subscription is pending; the next request for it re-reads once the lease runs out, before any create.
    /// </summary>
    private async Task<SubscribeResult> SettleSubscriptionAsync(int customerId, SubscriptionPlan plan, string reference, BillingClaim claim)
    {
        var (settled, found) = await RereadAsync(t => FindSubscriptionWithFallbackAsync(reference, customerId, t));
        if (settled && found?.Id is int subscriptionId)
        {
            await _claims.CompleteSubscriptionAsync(claim, customerId, subscriptionId);
            _logger.LogInformation("Settled subscription {Reference}: created as {SubscriptionId}.", reference, subscriptionId);
            return new SubscribeResult(SubscribeStatus.Created, reference, ToSummary(found, plan.Handle, plan.Name));
        }

        await _claims.MarkUnknownAsync(claim);
        _logger.LogWarning("Subscription {Reference} outcome is unknown ({Reason}); reported as pending.",
            reference, settled ? "not found yet" : "lookup failed");
        return new SubscribeResult(SubscribeStatus.Pending, reference, null);
    }

    private async Task<Subscription?> FindSubscriptionWithFallbackAsync(string reference, int customerId, CancellationToken ct)
    {
        try
        {
            return await FindSubscriptionAsync(reference, ct);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(ex, "FindSubscription for {Reference} failed; falling back to the customer's subscription list.", reference);
            var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
            return subscriptions.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal));
        }
    }

    // ---- Customer ----------------------------------------------------------------------------------------

    /// <summary>
    /// Ensures a Maxio customer exists for the buyer and returns its id, or null when another request is creating
    /// it or its creation could not be settled yet.
    /// </summary>
    private async Task<int?> EnsureCustomerAsync(BillingBuyer buyer, CancellationToken ct)
    {
        var reference = BillingReferences.ForCustomer(buyer.UserName);
        var (acquired, claim) = await _claims.TryAcquireAsync(reference, BillingClaimKind.Customer, buyer.UserName, null, ct);
        if (!acquired)
        {
            if (claim.State == BillingClaimState.Completed && claim.ProviderCustomerId is int knownId)
            {
                return knownId;
            }
            if (!await TryTakeOverAsync(claim, ct))
            {
                _logger.LogInformation("Customer {Reference} is being created by another request.", reference);
                return null;
            }
        }

        Customer? existing;
        try
        {
            existing = await ReadCustomerByReferenceAsync(reference, ct);
        }
        catch
        {
            await _claims.ReleaseAsync(claim);
            throw;
        }

        if (existing is not null)
        {
            var id = RequireCustomerId(existing);
            await _claims.CompleteCustomerAsync(claim, id);
            return id;
        }

        return await CreateCustomerAsync(buyer, reference, claim, ct);
    }

    private async Task<int?> CreateCustomerAsync(BillingBuyer buyer, string reference, BillingClaim claim, CancellationToken ct)
    {
        var email = string.IsNullOrWhiteSpace(buyer.Email) ? buyer.UserName : buyer.Email.Trim();
        var localPart = email.Split('@')[0];
        var request = new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    // eShop identities carry no personal names; Maxio requires both.
                    FirstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart,
                    LastName = "eShopOnWeb",
                    Email = email,
                    // Maxio allows one customer per reference: this is the provider-side idempotency key.
                    Reference = reference
                }
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(request, cancellationToken: ct);
            if (response.Customer?.Id is int customerId)
            {
                await _claims.CompleteCustomerAsync(claim, customerId);
                _logger.LogInformation("Created Maxio customer {CustomerId} ({Reference}).", customerId, reference);
                return customerId;
            }

            _logger.LogWarning("CreateCustomer for {Reference} succeeded without a customer id; settling by lookup.", reference);
            return await SettleCustomerAsync(reference, claim, rejection: null);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var rejection))
            {
                // A 422 may be the reference-uniqueness refusal for a customer an earlier attempt created: look first.
                var errors = DescribeCustomerErrors(rejection);
                _logger.LogWarning("Maxio rejected customer {Reference} (HTTP {StatusCode}): {Errors}; checking for an existing customer.",
                    reference, (int)ex.StatusCode, string.Join("; ", errors));
                return await SettleCustomerAsync(reference, claim,
                    new BillingProviderException("The billing customer could not be created.", BillingFailureKind.Rejected,
                        (int)ex.StatusCode, errors, ex));
            }
            if (IsServerError(ex.StatusCode))
            {
                _logger.LogWarning(ex, "CreateCustomer for {Reference} failed with HTTP {StatusCode}; settling by lookup.",
                    reference, (int)ex.StatusCode);
                return await SettleCustomerAsync(reference, claim, rejection: null);
            }
            if (ex.Error.TryGetRawError(out _))
            {
                await _claims.ReleaseAsync(claim);
                throw FromStatus("CreateCustomer", ex.StatusCode, ex);
            }
            await _claims.ReleaseAsync(claim);
            throw FromStatus("CreateCustomer", ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex) when (IsSuccess(ex.StatusCode) || IsServerError(ex.StatusCode))
        {
            _logger.LogWarning(ex, "CreateCustomer for {Reference} returned an unreadable HTTP {StatusCode} body; settling by lookup.",
                reference, (int)ex.StatusCode);
            return await SettleCustomerAsync(reference, claim, rejection: null);
        }
        catch (ResponseDeserializationException ex)
        {
            await _claims.ReleaseAsync(claim);
            throw FromStatus("CreateCustomer", ex.StatusCode, ex);
        }
        catch (ApiException ex) when (IsServerError(ex.StatusCode))
        {
            _logger.LogWarning(ex, "CreateCustomer for {Reference} failed with HTTP {StatusCode}; settling by lookup.",
                reference, (int)ex.StatusCode);
            return await SettleCustomerAsync(reference, claim, rejection: null);
        }
        catch (ApiException ex)
        {
            await _claims.ReleaseAsync(claim);
            throw FromStatus("CreateCustomer", ex.StatusCode, ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "CreateCustomer for {Reference} lost its connection; settling by lookup.", reference);
            return await SettleCustomerAsync(reference, claim, rejection: null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("CreateCustomer for {Reference} was cancelled in flight; settling by lookup.", reference);
            await SettleCustomerAsync(reference, claim, rejection: null);
            // The request's budget is spent; the subscription step cannot run in it. Report pending.
            return null;
        }
        catch (AuthSchemeException ex)
        {
            await _claims.ReleaseAsync(claim);
            _logger.LogError(ex, "Maxio credentials could not be applied.");
            throw new BillingProviderException("Billing is temporarily unavailable.", BillingFailureKind.Unavailable, innerException: ex);
        }
    }

    /// <summary>
    /// Settles a CreateCustomer whose outcome is unknown (or that was refused) by looking the customer up by reference.
    /// Found → recorded. Definitively absent → the claim is released (Maxio's reference uniqueness refuses any late
    /// duplicate) and a refusal is rethrown. Lookup failed → the claim is marked Unknown and null (pending) returned.
    /// </summary>
    private async Task<int?> SettleCustomerAsync(string reference, BillingClaim claim, BillingProviderException? rejection)
    {
        var (settled, found) = await RereadAsync(t => ReadCustomerByReferenceAsync(reference, t));
        if (settled && found is not null)
        {
            var id = RequireCustomerId(found);
            await _claims.CompleteCustomerAsync(claim, id);
            _logger.LogInformation("Settled customer {Reference}: exists as {CustomerId}.", reference, id);
            return id;
        }

        if (settled)
        {
            await _claims.ReleaseAsync(claim);
            if (rejection is not null)
            {
                throw rejection;
            }
            _logger.LogWarning("Customer {Reference} was not created; the next request will retry.", reference);
            return null;
        }

        await _claims.MarkUnknownAsync(claim);
        _logger.LogWarning("Customer {Reference} outcome is unknown; reported as pending.", reference);
        return null;
    }

    // ---- My subscriptions --------------------------------------------------------------------------------

    private async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsCoreAsync(BillingBuyer buyer, CancellationToken ct)
    {
        var reference = BillingReferences.ForCustomer(buyer.UserName);
        var customerId = await _claims.FindCustomerIdAsync(reference, ct);
        if (customerId is null)
        {
            // No local mapping (e.g. after a restart of the in-memory store): ask Maxio. Never creates a customer.
            var customer = await ReadCustomerByReferenceAsync(reference, ct);
            if (customer is null)
            {
                return Array.Empty<SubscriptionSummary>();
            }
            customerId = RequireCustomerId(customer);
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, ct);
        var knownPlans = await _claims.GetSubscriptionPlansAsync(buyer.UserName, ct);

        return subscriptions
            .Where(s => s.Id is not null)
            .Select(s => ToSummary(s, knownPlans.TryGetValue(s.Id!.Value, out var handle) ? handle : null, null))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    // ---- Maxio reads -------------------------------------------------------------------------------------

    private Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct) =>
        CallAsync<Customer?>("ReadCustomerByReference", async t =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(
                    new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: t);
                return response.Customer ?? throw Unreadable("ReadCustomerByReference");
            }
            catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }, ct);

    private Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct) =>
        CallAsync<Subscription?>("FindSubscription", async t =>
        {
            try
            {
                var response = await _client.Subscriptions.FindSubscription(
                    new FindSubscriptionRequest { Reference = reference }, cancellationToken: t);
                return response.Subscription ?? throw Unreadable("FindSubscription");
            }
            catch (ApiException<FindSubscriptionError> ex)
            {
                if (ex.Error.TryGetNoContent(out _))
                {
                    return null; // 404: no subscription carries this reference
                }
                if (ex.Error.TryGetRawError(out _))
                {
                    throw FromStatus("FindSubscription", ex.StatusCode, ex);
                }
                throw FromStatus("FindSubscription", ex.StatusCode, ex);
            }
        }, ct);

    private Task<Subscription> ReadSubscriptionAsync(int subscriptionId, CancellationToken ct) =>
        CallAsync("ReadSubscription", async t =>
        {
            var response = await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: t);
            return response.Subscription ?? throw Unreadable("ReadSubscription");
        }, ct);

    private Task<List<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct) =>
        CallAsync("ListCustomerSubscriptions", async t =>
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: t);
            return response.Select(r => r?.Subscription).OfType<Subscription>().ToList();
        }, ct);

    // ---- Plumbing ----------------------------------------------------------------------------------------

    /// <summary>One budget for everything a single API request does against Maxio, linked to the caller's token.</summary>
    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(_options.CallBudgetSeconds));
        try
        {
            return await work(budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Maxio work exceeded the {Budget}s call budget.", _options.CallBudgetSeconds);
            throw new BillingProviderException("Billing did not respond in time; try again.", BillingFailureKind.Unavailable,
                innerException: ex);
        }
    }

    /// <summary>
    /// Re-reads Maxio with its own budget, independent of the request's (which may be what just ran out).
    /// Returns <c>Settled = false</c> when the re-read itself failed.
    /// </summary>
    private async Task<(bool Settled, T? Found)> RereadAsync<T>(Func<CancellationToken, Task<T?>> reread) where T : class
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ReconcileBudgetSeconds));
        try
        {
            return (true, await reread(budget.Token));
        }
        catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Re-reading Maxio to settle a write failed.");
            return (false, null);
        }
    }

    private async Task<bool> TryTakeOverAsync(BillingClaim claim, CancellationToken ct)
    {
        var lease = TimeSpan.FromSeconds(_options.ClaimLeaseSeconds);
        var takeable = claim.State == BillingClaimState.Released ||
                       (claim.State is BillingClaimState.Claimed or BillingClaimState.Unknown &&
                        claim.IsLeaseExpired(_clock.GetUtcNow(), lease));
        return takeable && await _claims.TryTakeOverAsync(claim, ct);
    }

    /// <summary>
    /// The shared catch ladder for Maxio calls. Operation-specific typed errors are handled inside
    /// <paramref name="call"/>; everything else the SDK raises becomes a <see cref="BillingProviderException"/>.
    /// </summary>
    private async Task<T> CallAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await call(ct);
        }
        catch (ApiException<RawError> ex)
        {
            throw FromStatus(operation, ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogError(ex, "Maxio {Operation} returned an unreadable HTTP {StatusCode} body.", operation, (int)ex.StatusCode);
            if (IsSuccess(ex.StatusCode))
            {
                throw new BillingProviderException("Billing returned a response that could not be processed.",
                    BillingFailureKind.ProviderError, (int)ex.StatusCode, innerException: ex);
            }
            throw FromStatus(operation, ex.StatusCode, ex);
        }
        catch (ApiException ex)
        {
            throw FromStatus(operation, ex.StatusCode, ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning(ex, "Maxio {Operation} timed out after {Timeout}.", operation, ex.Timeout);
            throw new BillingProviderException("Billing did not respond in time; try again.", BillingFailureKind.Unavailable,
                innerException: ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "Maxio {Operation} could not be reached.", operation);
            throw new BillingProviderException("Billing is temporarily unavailable.", BillingFailureKind.Unavailable,
                innerException: ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials could not be applied for {Operation}.", operation);
            throw new BillingProviderException("Billing is temporarily unavailable.", BillingFailureKind.Unavailable,
                innerException: ex);
        }
    }

    private BillingProviderException FromStatus(string operation, HttpStatusCode status, Exception ex,
        IReadOnlyList<string>? errors = null)
    {
        var code = (int)status;
        switch (code)
        {
            case 401 or 403:
                // Our credentials, not the caller's: never pass an auth failure through as the caller's.
                _logger.LogError(ex, "Maxio rejected our credentials on {Operation} (HTTP {StatusCode}).", operation, code);
                return new BillingProviderException("Billing is temporarily unavailable.", BillingFailureKind.Unavailable, code, errors, ex);
            case 429:
                _logger.LogWarning(ex, "Maxio throttled {Operation} (HTTP 429).", operation);
                return new BillingProviderException("Billing is busy; try again shortly.", BillingFailureKind.Unavailable, code, errors, ex);
            case >= 400 and < 500:
                _logger.LogWarning(ex, "Maxio refused {Operation} (HTTP {StatusCode}).", operation, code);
                return new BillingProviderException("Billing refused the request.", BillingFailureKind.Rejected, code, errors, ex);
            default:
                _logger.LogError(ex, "Maxio {Operation} failed (HTTP {StatusCode}).", operation, code);
                return new BillingProviderException("Billing failed to process the request.", BillingFailureKind.ProviderError, code, errors, ex);
        }
    }

    private static BillingProviderException Unreadable(string operation) =>
        new($"Billing returned an incomplete {operation} response.", BillingFailureKind.ProviderError);

    private static int RequireCustomerId(Customer customer) =>
        customer.Id ?? throw new BillingProviderException("Billing returned a customer without an id.", BillingFailureKind.ProviderError);

    private static IReadOnlyList<string> DescribeCustomerErrors(CustomerErrorResponse1 response)
    {
        if (response.Errors is null)
        {
            return Array.Empty<string>();
        }
        if (response.Errors.TryGetListOfString(out var list))
        {
            return list;
        }
        if (response.Errors.TryGetCustomerError(out var single) && !string.IsNullOrWhiteSpace(single.Customer))
        {
            return new[] { single.Customer };
        }
        return Array.Empty<string>();
    }

    private static SubscriptionSummary ToSummary(Subscription subscription, string? fallbackPlanHandle, string? fallbackPlanName) =>
        new(
            subscription.Id ?? 0,
            subscription.Product?.Handle ?? fallbackPlanHandle,
            subscription.Product?.Name ?? fallbackPlanName,
            subscription.ProductPriceInCents,
            subscription.Currency,
            subscription.State?.Value,
            // current_period_ends_at is when the next regularly scheduled charge happens; next_assessment_at only
            // diverges from it while a failed renewal is being retried, and is reported alongside.
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            subscription.NextAssessmentAt,
            subscription.CreatedAt,
            subscription.Reference);

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static bool IsServerError(HttpStatusCode status) => (int)status >= 500;
}
