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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioCustomer = MaxioAdvancedBilling.Models.Customer;
using MaxioProduct = MaxioAdvancedBilling.Models.Product;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// <para>
/// This is the one boundary where Maxio failures become <see cref="BillingException"/>s: every call site
/// applies the same catch ladder, so the same kind of failure always becomes the same outcome, and no
/// provider or framework exception text ever reaches a caller.
/// </para>
/// <para>
/// Nothing here holds a numeric Maxio id across requests. Plans are addressed by handle and the customer id
/// is re-derived from the shopper's stable reference on every request, so the integration survives a Maxio
/// re-seed that reassigns ids, and needs no local mapping table.
/// </para>
/// </remarks>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>Page size for the plan listing. The operation is not auto-paginating, so we drive it.</summary>
    private const int PlanPageSize = 100;

    /// <summary>Stops a misbehaving paging response from looping forever.</summary>
    private const int MaxPlanPages = 20;

    /// <summary>Total budget for a read endpoint, covering every Maxio call it makes.</summary>
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(30);

    /// <summary>Total budget for the subscribe flow (ensure customer, guard, create).</summary>
    private static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(60);

    /// <summary>Budget for the reconciliation read that settles an unknown write outcome.</summary>
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(15);

    /// <summary>Wire value of the collection method that attempts to charge a payment method on file.</summary>
    private const string AutomaticCollection = "automatic";

    private static readonly TimeSpan ProductFamilyCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SiteCacheDuration = TimeSpan.FromMinutes(30);

    /// <summary>
    /// States in which the shopper still has a usable entitlement. Taken from the SDK's own classification;
    /// <c>assessing</c> and <c>pending</c> are deliberately excluded because Maxio warns they may not always
    /// be exposed, so they are a poor basis for an access decision.
    /// </summary>
    private static readonly HashSet<string> EntitlingStates =
        new(StringComparer.OrdinalIgnoreCase) { "active", "trialing", "paused" };

    /// <summary>
    /// States that mean the subscription is gone for good, so a fresh subscribe request may create a new one.
    /// Everything else — including dunning states such as <c>past_due</c> — counts as "already subscribed",
    /// because creating a second recurring charge alongside one that still exists is never the right answer.
    /// </summary>
    private static readonly HashSet<string> TerminatedStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "cancelled", "expired", "failed_to_create" };

    /// <summary>
    /// Serialises concurrent subscribe requests for the same shopper, so a double-click cannot slip two
    /// requests past the "already subscribed?" guard. This covers a single instance; the guard itself
    /// (re-reading Maxio before creating) is what keeps the flow correct across instances.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriberLocks =
        new(StringComparer.Ordinal);

    private readonly IMaxioClientProvider _clientProvider;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioClientProvider clientProvider,
        IOptionsMonitor<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _clientProvider = clientProvider;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    private string ProductFamilyHandle
    {
        get
        {
            var handle = _settings.CurrentValue.ProductFamilyHandle;
            if (string.IsNullOrWhiteSpace(handle))
            {
                throw new BillingNotConfiguredException(
                    $"Subscription billing is not configured on this deployment. '{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}' is not configured.");
            }

            return handle.Trim();
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = CreateBudget(ReadBudget, cancellationToken);
        var ct = budget.Token;

        var client = _clientProvider.GetClient();
        var familyHandle = ProductFamilyHandle;
        var familyId = await GetProductFamilyIdAsync(client, familyHandle, ct, cancellationToken);
        var currency = (await TryGetSiteProfileAsync(client, ct, cancellationToken))?.Currency;

        var products = new List<MaxioProduct>();
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PlanPageSize,
                    ct: ct);
            }
            catch (BillingException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                // One branch per accessor the error type declares; TryGetRawError is not a catch-all and
                // therefore goes last.
                if (ex.Error.TryGetString(out var notFoundBody))
                {
                    _logger.LogError(
                        "Maxio ListProductsForProductFamily reported the product family {ProductFamilyId} as missing: {Body}",
                        familyId,
                        Truncate(notFoundBody));
                    throw new BillingException(
                        "The configured subscription catalog is not available.", 502);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError("ListProductsForProductFamily", raw);
                }

                throw Translate("ListProductsForProductFamily", ex);
            }
            catch (Exception ex)
            {
                throw Translate("ListProductsForProductFamily", ex);
            }

            products.AddRange(pageItems.Select(item => item.Product));

            if (pageItems.Count < PlanPageSize)
            {
                break;
            }
        }

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => ToPlan(p, currency))
            .OrderBy(p => p.Price ?? decimal.MaxValue)
            .ThenBy(p => p.Handle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException("A subscription plan handle is required.", 400);
        }

        planHandle = planHandle.Trim();

        using var budget = CreateBudget(SubscribeBudget, cancellationToken);
        var ct = budget.Token;

        var client = _clientProvider.GetClient();

        // Reject an unknown or out-of-catalog plan before touching customer state, so a typo cannot create a
        // Maxio customer as a side effect.
        var plan = await ReadPlanAsync(client, planHandle, ct, cancellationToken);

        var gate = SubscriberLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var customer = await EnsureCustomerAsync(client, subscriber, ct, cancellationToken);
            if (customer.Id is not int customerId)
            {
                _logger.LogError(
                    "Maxio returned a customer for reference {Reference} without an id.", subscriber.Reference);
                throw new BillingException("The billing system returned an incomplete customer record.", 502);
            }

            var existing = FindExistingSubscription(
                await ListSubscriptionsAsync(client, customerId, ct, cancellationToken),
                planHandle);

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Shopper {Reference} is already subscribed to {PlanHandle} (subscription {SubscriptionId}); returning the existing subscription.",
                    subscriber.Reference,
                    planHandle,
                    existing.Id);
                return new SubscribeResult(existing, AlreadySubscribed: true);
            }

            var created = await CreateSubscriptionAsync(client, customerId, planHandle, subscriber, ct, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for shopper {Reference} on plan {PlanHandle} ({PlanName}); state {State}, collection {CollectionMethod}, next billing {NextBillingDate}.",
                created.Subscription.Id,
                subscriber.Reference,
                planHandle,
                plan.Name,
                created.Subscription.State,
                created.Subscription.PaymentCollectionMethod,
                created.Subscription.NextBillingDate);
            return created;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        using var budget = CreateBudget(ReadBudget, cancellationToken);
        var ct = budget.Token;

        var client = _clientProvider.GetClient();

        var customer = await FindCustomerAsync(client, subscriber.Reference, ct, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            // No billing customer yet simply means the shopper has never subscribed.
            return Array.Empty<CustomerSubscription>();
        }

        return await ListSubscriptionsAsync(client, customerId, ct, cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------------
    // Maxio calls
    // ---------------------------------------------------------------------------------------------------

    private async Task<int> GetProductFamilyIdAsync(
        MaxioAdvancedBillingClient client,
        string familyHandle,
        CancellationToken ct,
        CancellationToken callerToken)
    {
        var cacheKey = $"maxio:product-family-id:{familyHandle}";
        if (_cache.TryGetValue(cacheKey, out int cached))
        {
            return cached;
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate("ListProductFamilies", ex);
        }

        var id = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(pf => pf is not null &&
                                  string.Equals(pf.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
            ?.Id;

        if (id is not int familyId)
        {
            _logger.LogError(
                "Maxio product family '{ProductFamilyHandle}' was not found on the configured site.", familyHandle);
            throw new BillingException("The configured subscription catalog is not available.", 502);
        }

        _cache.Set(cacheKey, familyId, ProductFamilyCacheDuration);
        return familyId;
    }

    /// <summary>
    /// Reads a plan by handle and confirms it belongs to the configured product family and is still sellable.
    /// </summary>
    private async Task<MaxioProduct> ReadPlanAsync(
        MaxioAdvancedBillingClient client,
        string planHandle,
        CancellationToken ct,
        CancellationToken callerToken)
    {
        MaxioProduct product;
        try
        {
            var response = await client.Products.ReadProductByHandle(planHandle, ct);
            product = response.Product;
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }
        catch (Exception ex)
        {
            throw Translate("ReadProductByHandle", ex);
        }

        var familyHandle = ProductFamilyHandle;
        if (!string.Equals(product.ProductFamily?.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Refused to subscribe to plan {PlanHandle}: it belongs to product family {ActualFamily}, not the configured {ConfiguredFamily}.",
                planHandle,
                product.ProductFamily?.Handle,
                familyHandle);
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        if (product.ArchivedAt is not null)
        {
            _logger.LogWarning("Refused to subscribe to archived plan {PlanHandle}.", planHandle);
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        return product;
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(
        MaxioAdvancedBillingClient client,
        string reference,
        CancellationToken ct,
        CancellationToken callerToken)
    {
        try
        {
            var response = await client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer;
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // A genuine miss: this shopper has no Maxio customer record yet. Note that this is matched on the
            // 404 status only — an unreadable response is never converted into "no such customer", because
            // that would turn a corrupt reply into a spurious create.
            return null;
        }
        catch (Exception ex)
        {
            throw Translate("ReadCustomerByReference", ex);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        MaxioAdvancedBillingClient client,
        SubscriberIdentity subscriber,
        CancellationToken ct,
        CancellationToken callerToken)
    {
        var existing = await FindCustomerAsync(client, subscriber.Reference, ct, callerToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                // Maxio permits only one customer per reference value, which is what makes this flow
                // idempotent without any local mapping table.
                Reference = subscriber.Reference
            }
        };

        try
        {
            var response = await client.Customers.CreateCustomer(body, ct);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for shopper {Reference}.",
                response.Customer.Id,
                subscriber.Reference);
            return response.Customer;
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // 422 lands in the typed slot, and that payload is a shared paging-shaped model that cannot carry
            // a customer validation message — TryGetRawError is false for this status too. The reliable
            // reading of a 422 here is "the reference already exists", so settle it by looking the customer
            // up again rather than by trying to parse a message out of the response.
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                _logger.LogWarning(
                    "Maxio rejected the customer creation for reference {Reference}; re-reading in case it already exists.",
                    subscriber.Reference);

                var raced = await FindCustomerAsync(client, subscriber.Reference, ct, callerToken);
                if (raced is not null)
                {
                    return raced;
                }

                throw new BillingException(
                    "The billing system rejected the customer details for this account.", 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError("CreateCustomer", raw);
            }

            throw Translate("CreateCustomer", ex);
        }
        catch (Exception ex)
        {
            throw Translate("CreateCustomer", ex);
        }
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        MaxioAdvancedBillingClient client,
        int customerId,
        CancellationToken ct,
        CancellationToken callerToken)
    {
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await client.Customers.ListCustomerSubscriptions(customerId, ct);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate("ListCustomerSubscriptions", ex);
        }

        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => ToCustomerSubscription(s!))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        MaxioAdvancedBillingClient client,
        int customerId,
        string planHandle,
        SubscriberIdentity subscriber,
        CancellationToken ct,
        CancellationToken callerToken)
    {
        var collectionMethod = ChooseCollectionMethod(
            await TryGetSiteProfileAsync(client, ct, callerToken));

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                // Handle-driven: numeric product ids are not stable across a Maxio re-seed.
                ProductHandle = planHandle,
                CustomerId = customerId,

                // No payment-profile members are set: these plans do not require a payment method, so there
                // is no card capture and no 3-D Secure step. That also means automatic collection of the
                // signup balance cannot succeed, so the subscription is billed by invoice instead.
                PaymentCollectionMethod = collectionMethod

                //
                // Subscription.Reference is deliberately left unset. Maxio documents uniqueness only for the
                // *customer* reference; if it also enforced it for subscriptions, a deterministic value would
                // permanently block re-subscribing after a cancellation. The list-based guard above is the
                // idempotency mechanism, and it is fully specified.
            }
        };

        try
        {
            // Hold the create to a single outbound send: the retry pipeline resends on a transport failure
            // regardless of verb, and a duplicate here is a second recurring charge.
            using (MaxioWriteGuard.BeginSingleSend())
            {
                var response = await client.Subscriptions.CreateSubscription(body, ct);

                if (response.Subscription is null)
                {
                    _logger.LogError(
                        "Maxio accepted the subscription for shopper {Reference} but returned no subscription body.",
                        subscriber.Reference);

                    // The write succeeded but we cannot describe it — settle it by re-reading rather than
                    // reporting a failure for something that did happen.
                    var settled = await ReconcileAsync(client, customerId, planHandle, subscriber);
                    if (settled is not null)
                    {
                        return new SubscribeResult(settled, AlreadySubscribed: false);
                    }

                    throw new BillingException(
                        "The billing system returned a response that could not be processed.", 502);
                }

                return new SubscribeResult(ToCustomerSubscription(response.Subscription), AlreadySubscribed: false);
            }
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var detail = string.Join("; ", errorList.Errors);
                _logger.LogWarning(
                    "Maxio rejected the subscription for shopper {Reference} on plan {PlanHandle}: {Detail}",
                    subscriber.Reference,
                    planHandle,
                    Truncate(detail));
                throw new BillingException(
                    $"The billing system rejected this subscription: {Truncate(detail, 300)}", 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError("CreateSubscription", raw);
            }

            throw Translate("CreateSubscription", ex);
        }
        catch (Exception ex) when (IsUnknownWriteOutcome(ex))
        {
            // The request may or may not have reached Maxio. Do not guess — re-read and let Maxio say.
            _logger.LogWarning(
                ex,
                "The subscribe request for shopper {Reference} on plan {PlanHandle} did not complete; reconciling against Maxio.",
                subscriber.Reference,
                planHandle);

            var settled = await ReconcileAsync(client, customerId, planHandle, subscriber);
            if (settled is not null)
            {
                return new SubscribeResult(settled, AlreadySubscribed: true);
            }

            if (callerToken.IsCancellationRequested)
            {
                throw;
            }

            throw new BillingException(
                "The billing system did not respond in time and the subscription was not created. Please try again.",
                504,
                ex);
        }
        catch (Exception ex)
        {
            throw Translate("CreateSubscription", ex);
        }
    }

    /// <summary>
    /// Re-reads Maxio to settle a write whose outcome is unknown. Runs on its own budget, deliberately
    /// detached from the caller's token, because the token may already be the reason the write is unresolved.
    /// </summary>
    private async Task<CustomerSubscription?> ReconcileAsync(
        MaxioAdvancedBillingClient client,
        int customerId,
        string planHandle,
        SubscriberIdentity subscriber)
    {
        try
        {
            using var cts = new CancellationTokenSource(ReconcileBudget);
            var subscriptions = await ListSubscriptionsAsync(client, customerId, cts.Token, CancellationToken.None);
            var match = FindExistingSubscription(subscriptions, planHandle);

            if (match is not null)
            {
                _logger.LogInformation(
                    "Reconciliation found subscription {SubscriptionId} for shopper {Reference} on plan {PlanHandle}; the write had taken effect.",
                    match.Id,
                    subscriber.Reference,
                    planHandle);
            }

            return match;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Reconciliation failed for shopper {Reference} on plan {PlanHandle}; the outcome of the subscribe request is unknown.",
                subscriber.Reference,
                planHandle);
            return null;
        }
    }

    /// <summary>
    /// Reads the site-level facts this integration needs: the display currency (products carry none) and the
    /// billing architecture, which decides the collection method a card-less subscription must use.
    /// </summary>
    /// <remarks>
    /// Best-effort by design. Both callers can proceed without it — the plan list simply omits the currency,
    /// and subscribe falls back to the site's own default collection method.
    /// </remarks>
    private async Task<MaxioSiteProfile?> TryGetSiteProfileAsync(
        MaxioAdvancedBillingClient client,
        CancellationToken ct,
        CancellationToken callerToken)
    {
        const string cacheKey = "maxio:site-profile";
        if (_cache.TryGetValue(cacheKey, out MaxioSiteProfile? cached))
        {
            return cached;
        }

        try
        {
            var site = (await client.Sites.ReadSite(ct)).Site;
            var profile = new MaxioSiteProfile(
                site.Currency,
                site.RelationshipInvoicingEnabled ?? false,
                site.DefaultPaymentCollectionMethod);

            _cache.Set(cacheKey, profile, SiteCacheDuration);
            return profile;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the Maxio site; falling back to site defaults.");
            return null;
        }
    }

    /// <summary>
    /// Picks the collection method for a subscription created with no payment profile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This integration deliberately captures no payment method, so a site whose default is
    /// <c>automatic</c> will try — and fail — to collect the signup balance. The documented lever is
    /// <c>payment_collection_method</c>, whose non-automatic value is named after the site's billing
    /// architecture: <c>remittance</c> on Relationship Invoicing, <c>invoice</c> on legacy Statements. The
    /// two are not valid on the same site, so the value is read from the site rather than assumed.
    /// </para>
    /// <para>
    /// When the site already defaults to a non-automatic method its configuration is left alone, and when the
    /// site could not be read nothing is sent, so the site default still applies.
    /// </para>
    /// </remarks>
    private static CollectionMethod? ChooseCollectionMethod(MaxioSiteProfile? site)
    {
        if (site is null)
        {
            return null;
        }

        var siteDefault = site.DefaultPaymentCollectionMethod;
        if (!string.IsNullOrWhiteSpace(siteDefault) &&
            !string.Equals(siteDefault, AutomaticCollection, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return site.RelationshipInvoicingEnabled ? CollectionMethod.Remittance : CollectionMethod.Invoice;
    }

    private sealed record MaxioSiteProfile(
        string? Currency,
        bool RelationshipInvoicingEnabled,
        string? DefaultPaymentCollectionMethod);

    // ---------------------------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------------------------

    private static SubscriptionPlan ToPlan(MaxioProduct product, string? currency) =>
        new(
            Handle: product.Handle!,
            Name: product.Name,
            Description: product.Description,
            Price: FromCents(product.PriceInCents),
            Currency: currency,
            IntervalCount: product.Interval,
            IntervalUnit: product.IntervalUnit?.Value,
            RequiresPaymentMethod: product.RequireCreditCard ?? false);

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription)
    {
        var state = subscription.State?.Value;

        return new CustomerSubscription(
            Id: subscription.Id ?? 0,
            PlanHandle: subscription.Product?.Handle,
            PlanName: subscription.Product?.Name,
            Price: FromCents(subscription.ProductPriceInCents),
            Currency: subscription.Currency,
            State: state,
            PaymentCollectionMethod: subscription.PaymentCollectionMethod?.Value,
            IsLive: state is not null && EntitlingStates.Contains(state),
            // Maxio's own guidance is that current_period_ends_at is the field that reflects the next
            // billing date; next_assessment_at is the fallback when a period end is not present.
            NextBillingDate: subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CreatedAt: subscription.CreatedAt);
    }

    private static CustomerSubscription? FindExistingSubscription(
        IReadOnlyList<CustomerSubscription> subscriptions,
        string planHandle) =>
        subscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !(s.State is not null && TerminatedStates.Contains(s.State)));

    private static decimal? FromCents(long? cents) => cents.HasValue ? cents.Value / 100m : null;

    // ---------------------------------------------------------------------------------------------------
    // Failure translation
    // ---------------------------------------------------------------------------------------------------

    private static CancellationTokenSource CreateBudget(TimeSpan budget, CancellationToken callerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(budget);
        return cts;
    }

    private static bool IsUnknownWriteOutcome(Exception ex) =>
        ex is HttpRequestException
            or MaxioDuplicateSendBlockedException
            or OperationCanceledException;

    /// <summary>
    /// Shared fallback for failures with no typed payload: transport faults, cancellation, unreadable bodies
    /// and <see cref="RawError"/> responses. Typed error payloads are always read at the call site, where the
    /// concrete error type is known.
    /// </summary>
    private BillingException Translate(string operation, Exception ex)
    {
        switch (ex)
        {
            case SdkException<RawError> sdkException:
                return FromRawError(operation, sdkException.Error);

            case MaxioDuplicateSendBlockedException:
                _logger.LogError(ex, "Maxio {Operation} was not retried to avoid a duplicate write.", operation);
                return new BillingException(
                    "The billing request could not be confirmed. Please check your subscriptions before retrying.",
                    502,
                    ex);

            case JsonException:
                // A 2xx whose body no longer matches the model: the outcome really is unknown, so this is a
                // server-side failure rather than a rejection the caller can act on.
                _logger.LogError(ex, "Maxio {Operation} returned a body that could not be deserialized.", operation);
                return new BillingException(
                    "The billing system returned a response that could not be processed.", 502, ex);

            case OperationCanceledException:
                _logger.LogError(ex, "Maxio {Operation} exceeded its time budget.", operation);
                return new BillingException("The billing system did not respond in time. Please try again.", 504, ex);

            case HttpRequestException:
                _logger.LogError(ex, "Maxio {Operation} could not reach the billing system.", operation);
                return new BillingException("The billing system is currently unavailable.", 502, ex);

            default:
                _logger.LogError(ex, "Maxio {Operation} failed unexpectedly.", operation);
                return new BillingException("The billing system is currently unavailable.", 502, ex);
        }
    }

    private BillingException FromRawError(string operation, RawError raw)
    {
        var status = (int)raw.StatusCode;
        _logger.LogError(
            "Maxio {Operation} returned HTTP {StatusCode}: {Body}", operation, status, Truncate(ReadBody(raw)));

        // Keep distinct failures distinct, but never hand the caller a status they cannot act on: an
        // authentication failure is this deployment's problem, not theirs.
        return status switch
        {
            401 or 403 => new BillingException("Subscription billing is not available right now.", 502),
            404 => new BillingException("The requested billing resource was not found.", 404),
            408 => new BillingException("The billing system did not respond in time. Please try again.", 504),
            409 => new BillingException("The billing system reported a conflict with the current state.", 409),
            429 => new BillingException("The billing system is busy. Please try again shortly.", 503),
            400 or 422 => new BillingException("The billing system rejected the request.", 422),
            _ => new BillingException("The billing system is currently unavailable.", 502)
        };
    }

    private static string ReadBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString() ?? string.Empty;
        }
        catch (Exception)
        {
            return "<unreadable>";
        }
    }

    private static string Truncate(string? value, int max = 1000)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max] + "…";
    }
}
