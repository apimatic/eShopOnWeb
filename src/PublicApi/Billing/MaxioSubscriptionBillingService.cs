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
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing.
/// <para>
/// Every write follows claim → SDK call → record: a durable claim (primary key in <see cref="IBillingClaimStore"/>)
/// is taken before the provider is called, so a double-click or a racing second request is refused by the store
/// and never reaches Maxio. A write whose outcome is ambiguous (connection dropped, timeout, unreadable 2xx) is
/// settled in its own catch by re-reading Maxio by the reference that was sent; if that re-read also fails the
/// claim is left <see cref="BillingClaimStatus.Unknown"/> and the next request for the same key settles it first.
/// </para>
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PlanCacheKey = "maxio:subscription-plans";
    private static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(15);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IBillingClaimStore _claims;
    private readonly IMemoryCache _cache;
    private readonly MaxioSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IBillingClaimStore claims,
        IMemoryCache cache,
        IOptions<MaxioSettings> settings,
        TimeProvider clock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _claims = claims;
        _cache = cache;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    private TimeSpan PendingTimeout => TimeSpan.FromSeconds(_settings.PendingClaimTimeoutSeconds);

    // ------------------------------------------------------------------ plans

    public async Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.PlanCacheSeconds > 0 && _cache.TryGetValue(PlanCacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        using var budget = Budget(cancellationToken);
        SubscriptionPlanCatalog catalog;
        try
        {
            catalog = await ReadPlanCatalogAsync(budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw BudgetExceeded(ex);
        }

        // A truncated listing is not cached: the next request tries again for the complete list.
        if (_settings.PlanCacheSeconds > 0 && !catalog.Truncated)
        {
            _cache.Set(PlanCacheKey, catalog, TimeSpan.FromSeconds(_settings.PlanCacheSeconds));
        }
        return catalog;
    }

    /// <summary>
    /// Walks the configured family's products page by page (the operation is not SDK-paged), bounded by
    /// <see cref="MaxioSettings.MaxPlanPages"/>. Hitting the cap with a full last page sets <c>Truncated</c>.
    /// </summary>
    private async Task<SubscriptionPlanCatalog> ReadPlanCatalogAsync(CancellationToken ct)
    {
        var familyId = "handle:" + _settings.ProductFamilyHandle!.Trim();
        var perPage = _settings.PlansPerPage;
        var plans = new List<SubscriptionPlan>();
        var seenHandles = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> items;
            try
            {
                items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = familyId,
                        Page = page,
                        PerPage = perPage
                    },
                    cancellationToken: ct);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                throw ex.Error.Match(
                    onString: message =>
                    {
                        _logger.LogError("Maxio ListProductsForProductFamily for family {FamilyHandle} returned {Status}: {Message}",
                            _settings.ProductFamilyHandle, (int)ex.StatusCode, Truncate(message));
                        return ProviderFailure("list plans", ex.StatusCode, ex);
                    },
                    otherwise: raw =>
                    {
                        _logger.LogError("Maxio ListProductsForProductFamily returned {Status}: {Body}", (int)ex.StatusCode, Truncate(raw.ReadAsString()));
                        return ProviderFailure("list plans", ex.StatusCode, ex);
                    });
            }
            catch (SdkException ex)
            {
                throw TranslateTransport("list plans", ex);
            }

            foreach (var item in items)
            {
                var product = item.Product;
                if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }
                if (!seenHandles.Add(product.Handle))
                {
                    continue; // a page boundary shifted under us; never offer a plan twice
                }
                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    product.PriceInCents ?? 0,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (items.Count < perPage)
            {
                break; // short (or empty) page: the listing is complete
            }
            if (page >= _settings.MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Plan listing for family {FamilyHandle} stopped at the {MaxPages}-page cap; result marked truncated.",
                    _settings.ProductFamilyHandle, _settings.MaxPlanPages);
                break;
            }
        }

        return new SubscriptionPlanCatalog(plans, truncated);
    }

    // ------------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(BillingUser user, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingFailure.InvalidPlan, "A plan handle is required.");
        }
        planHandle = planHandle.Trim();

        using var budget = Budget(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(user, planHandle, budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw BudgetExceeded(ex);
        }
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(BillingUser user, string planHandle, CancellationToken ct)
    {
        // Invariant: the plan must be one the configured family offers right now (fresh read, not the cache).
        var catalog = await ReadPlanCatalogAsync(ct);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new BillingException(BillingFailure.InvalidPlan, catalog.Truncated
                ? $"Plan '{planHandle}' could not be verified against the plan catalog. Try again later."
                : $"Plan '{planHandle}' is not an available subscription plan.");
        }

        var customerId = await EnsureCustomerAsync(user, ct);
        return await EnrollAsync(user, customerId, plan, ct);
    }

    /// <summary>
    /// Ensures exactly one Maxio customer exists for the user. Claim key <c>customer:{reference}</c>; the Maxio
    /// customer <c>reference</c> is derived deterministically from the user, and Maxio allows only one customer
    /// per reference, so a lost local claim (e.g. in-memory store restart) is recovered by lookup, not re-created.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(BillingUser user, CancellationToken ct)
    {
        var reference = CustomerReference(user.UserName);
        var key = "customer:" + reference;
        var now = _clock.GetUtcNow();

        var attempt = await _claims.TryClaimAsync(new BillingClaim(key, BillingClaimKind.Customer, user.UserName, reference, null, now), ct);
        var claim = attempt.Claim;
        if (!attempt.Acquired)
        {
            if (claim.Status == BillingClaimStatus.Completed && claim.ProviderCustomerId is int knownId)
            {
                return knownId;
            }
            if (!claim.IsStale(now, PendingTimeout))
            {
                throw InProgress();
            }
            // Stale pending / unknown outcome: take the claim over atomically, then settle by lookup below.
            if (!await _claims.TryUpdateAsync(claim, c => c.Restart(reference, _clock.GetUtcNow()), ct))
            {
                throw InProgress();
            }
        }

        // We own the claim. Look up first: the customer may exist from an earlier run or an unsettled attempt.
        try
        {
            var existing = await TryReadCustomerByReferenceAsync(reference, ct);
            if (existing is not null)
            {
                await CompleteAsync(claim, existing.Value, null);
                return existing.Value;
            }
        }
        catch
        {
            await ReleaseQuietlyAsync(claim);
            throw;
        }

        var (firstName, lastName) = NamesFor(user);
        CustomerResponse created;
        try
        {
            created = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = user.Email,
                            Reference = reference
                        }
                    }
                },
                cancellationToken: ct);
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            // Maxio refused the create. If the reason is that a customer with our reference already exists
            // (created concurrently by another deployment), that customer is ours: settle by lookup.
            int? settled;
            try
            {
                settled = await SettleCustomerAsync(claim, reference);
            }
            catch
            {
                await ReleaseQuietlyAsync(claim);
                throw;
            }
            if (settled is int id)
            {
                return id;
            }
            await ReleaseQuietlyAsync(claim);
            var details = ex.Error.Match(
                onCustomerErrorResponse1: body => CustomerErrorMessages(body),
                otherwise: raw => new[] { Truncate(raw.ReadAsString()) });
            _logger.LogWarning("Maxio CreateCustomer rejected for customer reference {CustomerReference} with {Status}: {Errors}",
                reference, (int)ex.StatusCode, string.Join("; ", details));
            throw RejectedOrProvider("create billing customer", ex.StatusCode, ex, details);
        }
        catch (Exception ex) when (IsAmbiguous(ex))
        {
            // The create may have landed. Settle by re-reading with the reference we sent.
            _logger.LogWarning(ex, "Maxio CreateCustomer outcome unknown for customer reference {CustomerReference}; re-reading.", reference);
            int? settled;
            try
            {
                settled = await SettleCustomerAsync(claim, reference);
            }
            catch (Exception rereadEx)
            {
                _logger.LogError(rereadEx, "Could not settle CreateCustomer for customer reference {CustomerReference}; claim left unknown.", reference);
                await MarkUnknownQuietlyAsync(claim);
                throw new BillingException(BillingFailure.OutcomeUnknown,
                    "The billing provider did not confirm the customer. Retry shortly; the request will be reconciled.", ex, reference: reference);
            }
            if (settled is int id)
            {
                return id;
            }
            await ReleaseQuietlyAsync(claim);
            throw TranslateTransport("create billing customer", ex);
        }
        catch (ApiException ex)
        {
            await ReleaseQuietlyAsync(claim);
            throw ProviderFailure("create billing customer", ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            await ReleaseQuietlyAsync(claim);
            throw TranslateTransport("create billing customer", ex);
        }

        var customerId = created.Customer?.Id;
        if (customerId is null)
        {
            // 2xx without an id: settle by lookup rather than guess.
            var settled = await SettleCustomerAsync(claim, reference);
            if (settled is int id) return id;
            await MarkUnknownQuietlyAsync(claim);
            throw new BillingException(BillingFailure.OutcomeUnknown, "The billing provider did not return the customer. Retry shortly.", reference: reference);
        }

        await CompleteAsync(claim, customerId.Value, null);
        _logger.LogInformation("Created Maxio customer {CustomerId} for customer reference {CustomerReference}.", customerId, reference);
        return customerId.Value;
    }

    /// <summary>Re-reads the customer by reference with a fresh budget; records it on the claim when found.</summary>
    private async Task<int?> SettleCustomerAsync(BillingClaim claim, string reference)
    {
        using var settle = new CancellationTokenSource(SettleBudget);
        var id = await TryReadCustomerByReferenceAsync(reference, settle.Token);
        if (id is not null)
        {
            await CompleteAsync(claim, id.Value, null);
        }
        return id;
    }

    /// <summary>Returns the Maxio customer id for the reference, or null when Maxio has no such customer.</summary>
    private async Task<int?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: ct);
            return response.Customer?.Id
                ?? throw new BillingException(BillingFailure.ProviderUnavailable, "The billing provider returned a customer without an id.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // a real "no such customer" — an unreadable body never lands here
        }
        catch (ApiException<RawError> ex)
        {
            _logger.LogError("Maxio ReadCustomerByReference returned {Status}: {Body}", (int)ex.StatusCode, Truncate(ex.Error.ReadAsString()));
            throw ProviderFailure("look up billing customer", ex.StatusCode, ex);
        }
        catch (ApiException ex)
        {
            throw ProviderFailure("look up billing customer", ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            throw TranslateTransport("look up billing customer", ex);
        }
    }

    /// <summary>
    /// Creates the subscription once per (customer, plan). Claim key <c>subscription:{customer reference}:{plan}</c>;
    /// the subscription <c>reference</c> sent to Maxio is generated when the claim is taken and is how an ambiguous
    /// create is found again.
    /// </summary>
    private async Task<SubscribeResult> EnrollAsync(BillingUser user, int customerId, SubscriptionPlan plan, CancellationToken ct)
    {
        var customerReference = CustomerReference(user.UserName);
        var key = $"subscription:{customerReference}:{plan.Handle}";
        var subscriptionReference = NewSubscriptionReference();
        var now = _clock.GetUtcNow();

        var attempt = await _claims.TryClaimAsync(
            new BillingClaim(key, BillingClaimKind.Subscription, user.UserName, subscriptionReference, plan.Handle, now), ct);
        var claim = attempt.Claim;

        if (!attempt.Acquired)
        {
            if (claim.Status == BillingClaimStatus.Pending && !claim.IsStale(now, PendingTimeout))
            {
                throw InProgress();
            }

            // Completed, unknown or stale: find out what Maxio holds for the reference on the claim.
            var previous = await FindSubscriptionAsync(claim.ProviderReference, ct);
            if (previous is not null && IsLive(previous))
            {
                if (claim.Status != BillingClaimStatus.Completed)
                {
                    await CompleteAsync(claim, customerId, previous.Id);
                }
                return new SubscribeResult(ToSummary(previous), Created: false);
            }

            // Nothing live behind the claim (never created, or since canceled/expired): take it over for a new attempt.
            if (!await _claims.TryUpdateAsync(claim, c => c.Restart(subscriptionReference, _clock.GetUtcNow()), ct))
            {
                throw InProgress();
            }
        }

        // We own the claim. Guard against a live subscription the local store no longer knows about
        // (in-memory store restarted, or created out of band) before creating another one.
        Subscription? alreadyLive;
        try
        {
            alreadyLive = (await ListCustomerSubscriptionsAsync(customerId, ct))
                .FirstOrDefault(s => IsLive(s) && string.Equals(s.Product?.Handle, plan.Handle, StringComparison.Ordinal));
        }
        catch
        {
            await ReleaseQuietlyAsync(claim);
            throw;
        }
        if (alreadyLive is not null)
        {
            await CompleteAsync(claim, customerId, alreadyLive.Id, alreadyLive.Reference);
            return new SubscribeResult(ToSummary(alreadyLive), Created: false);
        }

        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = CollectionMethodSetting()
                        }
                    }
                },
                cancellationToken: ct);
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            // A definite refusal: nothing was created. Release so the user can try again.
            await ReleaseQuietlyAsync(claim);
            var details = ex.Error.Match(
                onErrorListResponse1: body => body.Errors.ToArray(),
                otherwise: raw => new[] { Truncate(raw.ReadAsString()) });
            _logger.LogWarning("Maxio CreateSubscription rejected for subscription reference {SubscriptionReference} (plan {PlanHandle}, customer {CustomerId}) with {Status}: {Errors}",
                subscriptionReference, plan.Handle, customerId, (int)ex.StatusCode, string.Join("; ", details));
            throw RejectedOrProvider("create subscription", ex.StatusCode, ex, details);
        }
        catch (Exception ex) when (IsAmbiguous(ex))
        {
            _logger.LogWarning(ex, "Maxio CreateSubscription outcome unknown for subscription reference {SubscriptionReference}; re-reading.", subscriptionReference);
            return await SettleSubscriptionAsync(claim, customerId, subscriptionReference, ex);
        }
        catch (SdkException ex)
        {
            await ReleaseQuietlyAsync(claim);
            throw ex is ApiException api ? ProviderFailure("create subscription", api.StatusCode, ex) : TranslateTransport("create subscription", ex);
        }

        var subscription = response.Subscription;
        if (subscription?.Id is null)
        {
            // 2xx without a subscription: the outcome is not readable from the response, so re-read it.
            return await SettleSubscriptionAsync(claim, customerId, subscriptionReference, null);
        }

        await CompleteAsync(claim, customerId, subscription.Id);
        _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({PlanHandle}) for customer {CustomerId}, reference {SubscriptionReference}, state {State}.",
            subscription.Id, plan.Handle, customerId, subscriptionReference, subscription.State?.Value);
        return new SubscribeResult(ToSummary(subscription), Created: true);
    }

    /// <summary>
    /// Settles an ambiguous CreateSubscription in place: found → recorded and returned; definitely absent → claim
    /// released and a retryable failure; re-read failed → claim left Unknown for the next request to settle.
    /// </summary>
    private async Task<SubscribeResult> SettleSubscriptionAsync(BillingClaim claim, int customerId, string subscriptionReference, Exception? cause)
    {
        Subscription? found;
        try
        {
            using var settle = new CancellationTokenSource(SettleBudget);
            found = await FindSubscriptionAsync(subscriptionReference, settle.Token);
        }
        catch (Exception rereadEx)
        {
            _logger.LogError(rereadEx, "Could not settle CreateSubscription for subscription reference {SubscriptionReference}; claim left unknown.", subscriptionReference);
            await MarkUnknownQuietlyAsync(claim);
            throw new BillingException(BillingFailure.OutcomeUnknown,
                "The billing provider did not confirm the subscription. Retry shortly; the request will be reconciled.",
                cause ?? rereadEx, reference: subscriptionReference);
        }

        if (found is not null)
        {
            await CompleteAsync(claim, customerId, found.Id);
            _logger.LogInformation("Settled CreateSubscription for reference {SubscriptionReference}: subscription {SubscriptionId} exists.", subscriptionReference, found.Id);
            return new SubscribeResult(ToSummary(found), Created: true);
        }

        await ReleaseQuietlyAsync(claim);
        _logger.LogWarning("Settled CreateSubscription for reference {SubscriptionReference}: no subscription was created.", subscriptionReference);
        throw cause is SdkException sdk
            ? TranslateTransport("create subscription", sdk)
            : new BillingException(BillingFailure.ProviderUnavailable, "The billing provider did not create the subscription. Please try again.", cause);
    }

    /// <summary>FindSubscription by our reference; null when Maxio has none (its declared 404).</summary>
    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: ct);
            return response.Subscription?.Id is null ? null : response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            return ex.Error.Match<Subscription?>(
                onNoContent: _ => null,
                otherwise: raw =>
                {
                    _logger.LogError("Maxio FindSubscription returned {Status}: {Body}", (int)ex.StatusCode, Truncate(raw.ReadAsString()));
                    throw ProviderFailure("look up subscription", ex.StatusCode, ex);
                });
        }
        catch (ApiException ex)
        {
            throw ProviderFailure("look up subscription", ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            throw TranslateTransport("look up subscription", ex);
        }
    }

    // ------------------------------------------------------------------ my subscriptions

    public async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        using var budget = Budget(cancellationToken);
        try
        {
            return await ListSubscriptionsCoreAsync(user, budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw BudgetExceeded(ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsCoreAsync(BillingUser user, CancellationToken ct)
    {
        // The customer id comes only from the caller's own claim or a lookup by the caller's own reference.
        var reference = CustomerReference(user.UserName);
        var claim = await _claims.FindAsync("customer:" + reference, ct);
        var customerId = claim is { Status: BillingClaimStatus.Completed, ProviderCustomerId: int stored }
            ? stored
            : await TryReadCustomerByReferenceAsync(reference, ct);
        if (customerId is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, ct);
        return subscriptions
            .Select(ToSummary)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: ct);
            return responses
                .Select(r => r.Subscription)
                .Where(s => s?.Id is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (ApiException<RawError> ex)
        {
            _logger.LogError("Maxio ListCustomerSubscriptions for customer {CustomerId} returned {Status}: {Body}",
                customerId, (int)ex.StatusCode, Truncate(ex.Error.ReadAsString()));
            throw ProviderFailure("list subscriptions", ex.StatusCode, ex);
        }
        catch (ApiException ex)
        {
            throw ProviderFailure("list subscriptions", ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            throw TranslateTransport("list subscriptions", ex);
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>One total budget for every Maxio call made while serving a request, linked to the request's abort.</summary>
    private CancellationTokenSource Budget(CancellationToken requestAborted)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.RequestBudgetSeconds));
        return cts;
    }

    /// <summary>
    /// Deterministic, non-PII Maxio customer reference for an eShopOnWeb user: stable across restarts and
    /// unique per user name (Maxio enforces one customer per reference).
    /// </summary>
    internal static string CustomerReference(string userName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant()));
        return "eshop-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private CollectionMethod CollectionMethodSetting() => _settings.PaymentCollectionMethod switch
    {
        "automatic" => CollectionMethod.Automatic,
        "prepaid" => CollectionMethod.Prepaid,
        "invoice" => CollectionMethod.Invoice,
        _ => CollectionMethod.Remittance
    };

    private static string NewSubscriptionReference() => "eshop-sub-" + Guid.NewGuid().ToString("N");

    /// <summary>eShopOnWeb users have no first/last name; Maxio requires both.</summary>
    private static (string First, string Last) NamesFor(BillingUser user)
    {
        var localPart = user.Email.Split('@')[0];
        return (string.IsNullOrWhiteSpace(localPart) ? user.UserName : localPart, "eShopOnWeb");
    }

    /// <summary>States in which the subscription still exists for billing purposes (not ended).</summary>
    private static bool IsLive(Subscription subscription)
    {
        var state = subscription.State;
        if (state is null)
        {
            return true; // unknown → treat as live: never risk a duplicate
        }
        return !(state == SubscriptionState.Canceled || state == SubscriptionState.Expired || state == SubscriptionState.FailedToCreate);
    }

    private static SubscriptionSummary ToSummary(Subscription s) => new(
        s.Id ?? 0,
        s.Reference,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Currency,
        s.State?.Value,
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        s.CurrentPeriodEndsAt,
        s.CreatedAt);

    private async Task CompleteAsync(BillingClaim claim, int customerId, int? subscriptionId, string? reference = null)
    {
        // Recording must not be cut short by the caller's budget: the provider already acted.
        var recorded = await _claims.TryUpdateAsync(claim, c =>
        {
            if (reference is not null && c.Kind == BillingClaimKind.Subscription)
            {
                c.Restart(reference, _clock.GetUtcNow());
            }
            c.MarkCompleted(customerId, subscriptionId, _clock.GetUtcNow());
        }, CancellationToken.None);
        if (!recorded)
        {
            _logger.LogWarning("Billing claim {ClaimKey} changed while recording its result; the next request re-reads Maxio.", claim.Key);
        }
    }

    private async Task MarkUnknownQuietlyAsync(BillingClaim claim)
    {
        try
        {
            await _claims.TryUpdateAsync(claim, c => c.MarkUnknown(_clock.GetUtcNow()), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not mark billing claim {ClaimKey} unknown; it will be settled once its pending timeout passes.", claim.Key);
        }
    }

    private async Task ReleaseQuietlyAsync(BillingClaim claim)
    {
        try
        {
            await _claims.ReleaseAsync(claim, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not release billing claim {ClaimKey}; it will be taken over once its pending timeout passes.", claim.Key);
        }
    }

    /// <summary>
    /// True when a write may have reached Maxio but we cannot know its result: transport failure or timeout
    /// (SdkConnectionException covers SdkTimeoutException), a provider 5xx, an unreadable 2xx, or our own
    /// budget/request cancellation after the request may have been sent.
    /// </summary>
    private static bool IsAmbiguous(Exception ex) => ex switch
    {
        SdkConnectionException => true,
        ResponseDeserializationException rde => (int)rde.StatusCode is >= 200 and < 300 || (int)rde.StatusCode >= 500,
        ApiException api => (int)api.StatusCode >= 500,
        OperationCanceledException => true,
        _ => false
    };

    private static BillingException BudgetExceeded(Exception inner) =>
        new(BillingFailure.ProviderUnavailable, "The billing provider did not respond in time. Please try again.", inner);

    private static BillingException InProgress() =>
        new(BillingFailure.InProgress, "A subscription request for this account is already in progress. Please wait a moment and try again.");

    private static BillingException RejectedOrProvider(string operation, HttpStatusCode status, Exception inner, IReadOnlyList<string> details) =>
        (int)status is 400 or 404 or 409 or 422
            ? new BillingException(BillingFailure.Rejected, $"The billing provider rejected the request to {operation}.", inner, details)
            : ProviderFailure(operation, status, inner);

    private static BillingException ProviderFailure(string operation, HttpStatusCode status, Exception inner) =>
        (int)status is 401 or 403
            ? new BillingException(BillingFailure.ProviderMisconfigured, "Billing is temporarily unavailable.", inner)
            : new BillingException(BillingFailure.ProviderUnavailable, $"The billing provider could not {operation}. Please try again.", inner);

    private static BillingException TranslateTransport(string operation, Exception ex) => ex switch
    {
        AuthSchemeException => new BillingException(BillingFailure.ProviderMisconfigured, "Billing is temporarily unavailable.", ex),
        ApiException api => ProviderFailure(operation, api.StatusCode, ex),
        _ => new BillingException(BillingFailure.ProviderUnavailable, $"The billing provider could not {operation} in time. Please try again.", ex)
    };

    private static IReadOnlyList<string> CustomerErrorMessages(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return Array.Empty<string>();
        }
        return body.Errors.Match<IReadOnlyList<string>>(
            onCustomerError: e => e.Customer is null ? Array.Empty<string>() : new[] { e.Customer },
            onListOfString: list => list,
            otherwise: raw => new[] { Truncate(raw.ToString()) });
    }

    private static string Truncate(string? value) =>
        value is null ? string.Empty : value.Length <= 500 ? value : value[..500] + "…";
}
