using System.Globalization;
using System.Net;
using System.Text.Json;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.MaxioBilling.Configuration;
using Microsoft.eShopWeb.MaxioBilling.Exceptions;
using Microsoft.eShopWeb.MaxioBilling.Interfaces;
using Microsoft.eShopWeb.MaxioBilling.Internal;
using Microsoft.eShopWeb.MaxioBilling.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioCustomer = MaxioAdvancedBilling.Models.Customer;
using MaxioProduct = MaxioAdvancedBilling.Models.Product;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.MaxioBilling.Services;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// This type is the integration boundary: every SDK exception, transport failure and unreadable
/// payload is translated into a <see cref="BillingException"/> here and nowhere else.
/// </summary>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Subscription states that mean "this user already has this plan", so a repeat request must not
    /// create a second subscription. The terminal states (canceled, expired, failed_to_create,
    /// trial_ended) are deliberately excluded so a user can re-subscribe after cancelling.
    /// <para>
    /// This set exists only to stop a duplicate create. Maxio documents "pending" and "assessing"
    /// as transient states that may not always be exposed and must not drive access decisions, so
    /// this must not be reused to decide whether a user has entitlement to anything.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "assessing", "soft_failure",
        "past_due", "suspended", "paused", "unpaid", "on_hold", "awaiting_signup"
    };

    private const int PlanPageSize = 100;
    private const int MaxPlanPages = 50;
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);

    private readonly MaxioClientAccessor _accessor;
    private readonly MaxioBillingOptions _options;
    private readonly IMemoryCache _cache;
    private readonly SubscriberLocks _locks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioClientAccessor accessor,
        IOptions<MaxioBillingOptions> options,
        IMemoryCache cache,
        SubscriberLocks locks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _accessor = accessor;
        _options = options.Value;
        _cache = cache;
        _locks = locks;
        _logger = logger;
    }

    public bool IsConfigured => _accessor.IsConfigured;

    private MaxioAdvancedBillingClient Client => _accessor.Require();

    // ---------------------------------------------------------------- plans

    public Task<IReadOnlyList<PlanSummary>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        _accessor.Require();

        return BoundedAsync<IReadOnlyList<PlanSummary>>(async ct =>
        {
            var familyId = await ResolveProductFamilyIdAsync(ct);
            var siteProfile = await TryResolveSiteProfileAsync(ct);
            var products = await ListFamilyProductsAsync(familyId, ct);

            return products
                .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(product => MapPlan(product, siteProfile.Currency))
                .OrderBy(plan => plan.PriceInCents ?? long.MaxValue)
                .ThenBy(plan => plan.Handle, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    // ------------------------------------------------------------ subscribe

    public Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        _accessor.Require();

        var requestedHandle = string.IsNullOrWhiteSpace(planHandle)
            ? _options.DefaultPlanHandle
            : planHandle.Trim();

        if (string.IsNullOrWhiteSpace(requestedHandle))
        {
            throw new BillingException(
                BillingFailureKind.Rejected,
                "No plan was requested and no default subscription plan is configured.");
        }

        return BoundedAsync(async ct =>
        {
            // Resolved against the configured product family, so a caller cannot subscribe to an
            // arbitrary product that happens to exist on the Maxio site.
            var plan = await GetPlanByHandleAsync(requestedHandle, ct);

            // Held across the check-then-create below: Maxio documents no uniqueness for a
            // subscription and offers no idempotency key, so this is what makes a double-click safe.
            using var subscriberLock = await _locks.AcquireAsync(subscriber.Reference, ct);

            var customerId = await EnsureCustomerAsync(subscriber, ct);

            var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscriber {Reference} already holds subscription {SubscriptionId} on plan '{PlanHandle}'; not creating another.",
                    subscriber.Reference, existing.Id, plan.Handle);
                return new SubscribeResult(existing, AlreadyExisted: true);
            }

            var created = await CreateSubscriptionAsync(subscriber, customerId, plan, ct);
            return new SubscribeResult(created, AlreadyExisted: false);
        }, cancellationToken);
    }

    // ---------------------------------------------------- my subscriptions

    public Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        _accessor.Require();

        return BoundedAsync<IReadOnlyList<SubscriptionSummary>>(async ct =>
        {
            var customer = await FindCustomerAsync(subscriber.Reference, ct);
            if (customer?.Id is null)
            {
                // Never subscribed: an empty list, not an error.
                return Array.Empty<SubscriptionSummary>();
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, ct);

            return subscriptions
                .Select(MapSubscription)
                .OrderByDescending(subscription => subscription.Id ?? 0)
                .ToList();
        }, cancellationToken);
    }

    // ------------------------------------------------------------- catalog

    /// <summary>
    /// Resolves the configured product-family handle to its numeric id.
    /// Maxio's read-by-id operation only accepts an int, so the family list is matched on handle.
    /// That list is not pageable, so a handle that is genuinely present but truncated out of the
    /// response fails loudly here rather than silently falling back to the whole site's catalog.
    /// </summary>
    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var handle = _options.ProductFamilyHandle!;
        var cacheKey = $"maxio:product-family-id:{handle}";

        if (_cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        var families = await ReadAsync(
            () => Client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct),
            "list product families");

        var match = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family =>
                family is not null &&
                string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            throw new BillingException(
                BillingFailureKind.Configuration,
                $"No Maxio product family with handle '{handle}' was found on the configured site.");
        }

        var id = match.Id.Value.ToString(CultureInfo.InvariantCulture);
        _cache.Set(cacheKey, id, TimeSpan.FromSeconds(Math.Max(1, _options.CatalogCacheSeconds)));
        return id;
    }

    /// <summary>
    /// The site facts this integration needs: currency (not carried on a product) and the billing
    /// architecture that decides which payment collection methods are even valid.
    /// </summary>
    private sealed record SiteProfile(string? Currency, bool? RelationshipInvoicingEnabled);

    /// <summary>
    /// Reads and caches the site profile. Currency is presentation detail only, so a failure here
    /// degrades rather than failing the plan list; callers that need the architecture check the
    /// nullable flag.
    /// </summary>
    private async Task<SiteProfile> TryResolveSiteProfileAsync(CancellationToken ct)
    {
        const string cacheKey = "maxio:site-profile";
        if (_cache.TryGetValue(cacheKey, out SiteProfile? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var site = await ReadAsync(() => Client.Sites.ReadSite(ct: ct), "read site");

            _logger.LogInformation(
                "Maxio site: test={IsTestSite}, currency={Currency}, relationshipInvoicing={RelationshipInvoicing}, defaultCollectionMethod={DefaultCollectionMethod}.",
                site.Site.Test, site.Site.Currency, site.Site.RelationshipInvoicingEnabled,
                site.Site.DefaultPaymentCollectionMethod);

            var profile = new SiteProfile(site.Site.Currency, site.Site.RelationshipInvoicingEnabled);
            _cache.Set(cacheKey, profile, TimeSpan.FromSeconds(Math.Max(1, _options.CatalogCacheSeconds)));
            return profile;
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex, "Could not read the Maxio site profile; plan prices will be returned without a currency.");
            return new SiteProfile(null, null);
        }
    }

    /// <summary>
    /// Decides the <c>payment_collection_method</c> sent on a create.
    /// <para>
    /// This application captures no card, so leaving the site default (<c>automatic</c>) in place
    /// makes Maxio try to collect the whole balance at signup and reject the subscription with
    /// "No payment method was on file" - even when the product's <c>require_credit_card</c> is
    /// false. Maxio's own documentation attributes the demand to product options and never
    /// mentions this field, so the link is established empirically against the sandbox rather than
    /// promised by the API: hence the explicit override, and the fact that it is configurable.
    /// </para>
    /// <para>
    /// The valid members differ by billing architecture - <c>remittance</c> on Relationship
    /// Invoicing, <c>invoice</c> on legacy Statements, each rejected on the other - so "auto"
    /// derives it from the site rather than hardcoding a literal that would be wrong elsewhere.
    /// </para>
    /// </summary>
    private async Task<CollectionMethod?> ResolveCollectionMethodAsync(CancellationToken ct)
    {
        var configured = _options.PaymentCollectionMethod?.Trim();

        if (string.IsNullOrWhiteSpace(configured) ||
            string.Equals(configured, MaxioBillingOptions.CollectionMethodAuto, StringComparison.OrdinalIgnoreCase))
        {
            var profile = await TryResolveSiteProfileAsync(ct);

            return profile.RelationshipInvoicingEnabled switch
            {
                true => CollectionMethod.Remittance,
                false => CollectionMethod.Invoice,
                // The site could not be read: send nothing rather than guess an invalid member.
                null => null
            };
        }

        if (string.Equals(configured, MaxioBillingOptions.CollectionMethodSiteDefault, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Matched explicitly rather than through FromValue, so a typo fails here with a clear
        // configuration error instead of being sent to Maxio as an unknown value.
        return configured.ToLowerInvariant() switch
        {
            "automatic" => CollectionMethod.Automatic,
            "remittance" => CollectionMethod.Remittance,
            "prepaid" => CollectionMethod.Prepaid,
            "invoice" => CollectionMethod.Invoice,
            _ => throw new BillingException(
                BillingFailureKind.Configuration,
                $"'{MaxioBillingOptions.SectionName}:{nameof(MaxioBillingOptions.PaymentCollectionMethod)}' is not a supported value.")
        };
    }

    /// <summary>Pages through every non-archived product in the family.</summary>
    private async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyId, CancellationToken ct)
    {
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> response;
            try
            {
                response = await Client.ProductFamilies.ListProductsForProductFamily(
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
                    perPage: PlanPageSize,
                    ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    _logger.LogError(
                        "Maxio reported the configured product family as missing: {Detail}", notFound);
                    throw new BillingException(
                        BillingFailureKind.Configuration,
                        "The configured Maxio product family could not be read.",
                        ex,
                        (int)HttpStatusCode.NotFound);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRawError(raw, "list plans", ex);
                }

                throw new BillingException(
                    BillingFailureKind.ProviderError,
                    "The billing provider returned an unrecognised error while listing plans.",
                    ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw UnreadableResponse("list plans", ex);
            }
            catch (HttpRequestException ex)
            {
                throw Unreachable("list plans", ex);
            }

            // ProductResponse.Product is a required member, so it is never null here.
            products.AddRange(response.Select(item => item.Product));

            if (response.Count < PlanPageSize)
            {
                return products;
            }
        }

        _logger.LogWarning(
            "Stopped paging Maxio plans after {MaxPages} pages; the plan list may be incomplete.", MaxPlanPages);
        return products;
    }

    private async Task<PlanSummary> GetPlanByHandleAsync(string handle, CancellationToken ct)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);
        var siteProfile = await TryResolveSiteProfileAsync(ct);
        var products = await ListFamilyProductsAsync(familyId, ct);

        var product = products.FirstOrDefault(candidate =>
            candidate.ArchivedAt is null &&
            string.Equals(candidate.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (product is null)
        {
            throw new BillingException(
                BillingFailureKind.PlanNotFound,
                $"No subscription plan with handle '{handle}' is available.");
        }

        return MapPlan(product, siteProfile.Currency);
    }

    // ------------------------------------------------------------ customers

    /// <summary>
    /// Looks the customer up by reference and creates one only if absent. Maxio enforces the
    /// reference as unique per site, so this is the idempotent half of "ensure a customer exists".
    /// </summary>
    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(subscriber.Reference, ct);
        if (existing?.Id is not null)
        {
            return existing.Id.Value;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }
        };

        try
        {
            CustomerResponse created;
            using (SingleSendScope.Begin())
            {
                created = await Client.Customers.CreateCustomer(body: body, ct: ct);
            }

            if (created.Customer.Id is null)
            {
                throw new BillingException(
                    BillingFailureKind.ProviderError,
                    "The billing provider created a customer but did not return its identifier.");
            }

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for subscriber {Reference}.",
                created.Customer.Id, subscriber.Reference);

            return created.Customer.Id.Value;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent request may have created the customer between the lookup and this create.
            // The 422 payload cannot tell us why it failed, so re-query instead of parsing text.
            var reread = await FindCustomerAsync(subscriber.Reference, ct);
            if (reread?.Id is not null)
            {
                _logger.LogInformation(
                    "Maxio customer {CustomerId} for subscriber {Reference} already existed; reusing it.",
                    reread.Id, subscriber.Reference);
                return reread.Id.Value;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
            {
                var detail = DescribeCustomerValidation(validation);
                _logger.LogWarning("Maxio rejected the customer for subscriber {Reference}: {Detail}",
                    subscriber.Reference, detail ?? "(no detail)");

                throw new BillingException(
                    BillingFailureKind.Rejected,
                    detail ?? "The billing provider rejected the customer details.",
                    ex,
                    (int)HttpStatusCode.UnprocessableEntity);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "create customer", ex);
            }

            throw new BillingException(
                BillingFailureKind.ProviderError,
                "The billing provider returned an unrecognised error while creating the customer.",
                ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (MayHaveReachedProvider(ex))
        {
            // The create may already have taken effect. Settle it by re-reading, not by assuming.
            var reconciled = await ReconcileAsync(token => FindCustomerAsync(subscriber.Reference, token));
            if (reconciled?.Id is not null)
            {
                return reconciled.Id.Value;
            }

            throw ex is JsonException
                ? UnreadableResponse("create customer", ex)
                : Unreachable("create customer", ex);
        }
    }

    /// <summary>Reads a customer by reference; returns null only when Maxio says it does not exist.</summary>
    private async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "look up billing customer", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            // "I could not read the answer" is not "the customer does not exist". Mapping this onto
            // absence would turn a corrupt response into a duplicate customer.
            throw UnreadableResponse("look up billing customer", ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable("look up billing customer", ex);
        }
    }

    // -------------------------------------------------------- subscriptions

    private async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        // There is no customer filter on the top-level subscription list, so the nested route is used.
        // It exposes no paging parameters either, which is why the match below is defensive.
        var responses = await ReadAsync(
            () => Client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct),
            "list customer subscriptions");

        return responses
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => subscription!)
            .ToList();
    }

    private async Task<SubscriptionSummary?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);

        var match = subscriptions
            .Where(subscription =>
                string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                IsLive(subscription.State))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        return match is null ? null : MapSubscription(match);
    }

    private async Task<SubscriptionSummary> CreateSubscriptionAsync(
        SubscriberIdentity subscriber,
        int customerId,
        PlanSummary plan,
        CancellationToken ct)
    {
        var collectionMethod = await ResolveCollectionMethodAsync(ct);

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                // Selected by handle: handles are stable across a re-seed, numeric ids are not.
                ProductHandle = plan.Handle,
                CustomerId = customerId,
                // Decides whether Maxio invoices or tries to charge a card at signup. Null means
                // "use the site default". See ResolveCollectionMethodAsync for why this is set.
                PaymentCollectionMethod = collectionMethod,
                // Wire name is "ref". Traceability only - Maxio documents no uniqueness for it,
                // so it is never used as an idempotency key.
                Reference = BuildSubscriptionReference(subscriber, plan.Handle)
                // No payment-profile fields: nothing is captured, and no request field exists that
                // suppresses a payment-method demand outright.
            }
        };

        try
        {
            SubscriptionResponse response;
            using (SingleSendScope.Begin())
            {
                response = await Client.Subscriptions.CreateSubscription(body: body, ct: ct);
            }

            if (response.Subscription is null)
            {
                throw new BillingException(
                    BillingFailureKind.ProviderError,
                    "The billing provider accepted the subscription but did not return it.");
            }

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on plan '{PlanHandle}' for subscriber {Reference}; state={State}, collectionMethod={CollectionMethod}.",
                response.Subscription.Id, plan.Handle, subscriber.Reference,
                response.Subscription.State?.Value, collectionMethod?.Value ?? "(site default)");

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var detail = string.Join(" ", errors.Errors.Where(message => !string.IsNullOrWhiteSpace(message)));
                _logger.LogWarning(
                    "Maxio rejected the subscription on plan '{PlanHandle}' for subscriber {Reference}: {Detail}",
                    plan.Handle, subscriber.Reference, string.IsNullOrWhiteSpace(detail) ? "(no detail)" : detail);

                throw new BillingException(
                    BillingFailureKind.Rejected,
                    string.IsNullOrWhiteSpace(detail)
                        ? "The billing provider rejected the subscription request."
                        : detail,
                    ex,
                    (int)HttpStatusCode.UnprocessableEntity);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "create subscription", ex);
            }

            throw new BillingException(
                BillingFailureKind.ProviderError,
                "The billing provider returned an unrecognised error while creating the subscription.",
                ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (MayHaveReachedProvider(ex))
        {
            // A request that failed on the way out may still have been received, so the outcome is
            // unknown rather than failed. Settle it against Maxio before answering the caller.
            _logger.LogWarning(ex,
                "Subscription create for subscriber {Reference} on plan '{PlanHandle}' failed with an unknown outcome; reconciling.",
                subscriber.Reference, plan.Handle);

            var reconciled = await ReconcileAsync(token => FindLiveSubscriptionAsync(customerId, plan.Handle, token));
            if (reconciled is not null)
            {
                _logger.LogInformation(
                    "Reconciled subscription {SubscriptionId} for subscriber {Reference} after an unknown outcome.",
                    reconciled.Id, subscriber.Reference);
                return reconciled;
            }

            throw new BillingException(
                ex is JsonException ? BillingFailureKind.ProviderError : BillingFailureKind.OutcomeUnknown,
                "The subscription could not be confirmed with the billing provider. Check your subscriptions before retrying.",
                ex);
        }
    }

    private static string BuildSubscriptionReference(SubscriberIdentity subscriber, string planHandle) =>
        $"{subscriber.Reference}:{planHandle}";

    private static bool IsLive(SubscriptionState? state) =>
        state?.Value is { Length: > 0 } value && LiveSubscriptionStates.Contains(value);

    // -------------------------------------------------------------- mapping

    private static PlanSummary MapPlan(MaxioProduct product, string? currency) => new()
    {
        Id = product.Id,
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        HasTrial = product.TrialInterval is > 0,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit?.Value,
        TrialPriceInCents = product.TrialPriceInCents,
        SetupFeeInCents = product.InitialChargeInCents,
        // Maxio carries two sibling card flags; they are reported separately rather than collapsed,
        // because only require_credit_card blocks a create. The subscribe attempt settles the rest.
        PaymentMethodRequired = product.RequireCreditCard == true,
        PaymentMethodRequested = product.RequestCreditCard == true
    };

    private static SubscriptionSummary MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State?.Value,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents,
        Currency = subscription.Currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CustomerId = subscription.Customer?.Id,
        CustomerReference = subscription.Customer?.Reference,
        Reference = subscription.Reference
    };

    private static string? DescribeCustomerValidation(CustomerErrorResponse1 validation)
    {
        // The generated payload only models two unrelated members, so any real per-field message is
        // dropped on deserialize. Best-effort by design, with a generic fallback at the call site.
        var messages = new List<string>();
        if (validation.Errors?.PerPage is { Count: > 0 } perPage)
        {
            messages.AddRange(perPage);
        }

        if (validation.Errors?.PricePoint is { Count: > 0 } pricePoint)
        {
            messages.AddRange(pricePoint);
        }

        var detail = string.Join(" ", messages.Where(message => !string.IsNullOrWhiteSpace(message)));
        return string.IsNullOrWhiteSpace(detail) ? null : detail;
    }

    // ------------------------------------------------------- call plumbing

    /// <summary>
    /// Gives every operation one whole-call budget, linked to the caller's token so a disconnected
    /// client also stops the outbound work. The SDK's own timeouts bound a single attempt only.
    /// </summary>
    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.CallBudgetSeconds)));

        try
        {
            return await operation(cts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingException(
                BillingFailureKind.ProviderUnavailable,
                "The billing provider did not respond in time.",
                ex);
        }
    }

    /// <summary>
    /// Runs a settle-the-outcome read on its own small budget, so a write that failed because the
    /// call budget expired can still be reconciled.
    /// </summary>
    private static async Task<T?> ReconcileAsync<T>(Func<CancellationToken, Task<T?>> read)
    {
        using var cts = new CancellationTokenSource(ReconcileBudget);
        try
        {
            return await read(cts.Token);
        }
        catch (Exception)
        {
            // The reconcile is best-effort; its failure must not mask the original outcome.
            return default;
        }
    }

    /// <summary>Wraps an operation whose only error case is an untyped <see cref="RawError"/>.</summary>
    private async Task<T> ReadAsync<T>(Func<Task<T>> call, string operationName)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, operationName, ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse(operationName, ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(operationName, ex);
        }
    }

    /// <summary>
    /// True for failures after which a write may or may not have been applied: a refused re-send, a
    /// transport fault, or a response body that could not be read.
    /// </summary>
    private static bool MayHaveReachedProvider(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is WriteAlreadySentException or HttpRequestException or JsonException)
            {
                return true;
            }
        }

        return false;
    }

    private BillingException TranslateRawError(RawError raw, string operationName, Exception inner)
    {
        var status = (int)raw.StatusCode;

        var kind = status switch
        {
            401 or 403 => BillingFailureKind.Configuration,
            408 or 429 => BillingFailureKind.ProviderUnavailable,
            >= 500 => BillingFailureKind.ProviderUnavailable,
            >= 400 => BillingFailureKind.Rejected,
            _ => BillingFailureKind.ProviderError
        };

        var message = kind switch
        {
            BillingFailureKind.Configuration =>
                "The billing provider rejected this server's credentials.",
            BillingFailureKind.ProviderUnavailable =>
                "The billing provider is currently unavailable. Please try again shortly.",
            BillingFailureKind.Rejected =>
                "The billing provider rejected the request.",
            _ => "The billing provider returned an unexpected response."
        };

        // The raw body is provider text of unknown shape: log it, never put it on the wire.
        _logger.LogWarning(
            "Maxio returned HTTP {StatusCode} for '{Operation}': {Body}",
            status, operationName, SafeReadBody(raw));

        return new BillingException(kind, message, inner, status);
    }

    private static string SafeReadBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body)
                ? "(empty body)"
                : body.Length > 2000 ? body[..2000] + "..." : body;
        }
        catch (Exception)
        {
            return "(unreadable body)";
        }
    }

    private BillingException UnreadableResponse(string operationName, Exception inner)
    {
        _logger.LogError(inner, "Could not read the Maxio response for '{Operation}'.", operationName);
        return new BillingException(
            BillingFailureKind.ProviderError,
            "The billing provider returned a response that could not be processed.",
            inner);
    }

    private BillingException Unreachable(string operationName, Exception inner)
    {
        _logger.LogError(inner, "Could not reach Maxio for '{Operation}'.", operationName);
        return new BillingException(
            BillingFailureKind.ProviderUnavailable,
            "The billing provider could not be reached. Please try again shortly.",
            inner);
    }
}
