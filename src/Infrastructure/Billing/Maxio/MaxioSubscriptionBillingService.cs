using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Exceptions;
using AdvancedBilling.Standard.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// <para>
/// Advanced Billing is the system of record — eShopOnWeb stores no local copy of who is subscribed to
/// what. That is deliberate: a local mirror would need reconciliation for every state change Advanced
/// Billing makes on its own (renewal, dunning, cancellation), and the app has nothing useful to say about
/// those. Every read here goes to Advanced Billing and reports what it currently believes.
/// </para>
/// <para>
/// Subscribing is idempotent in three layers, weakest to strongest:
/// a per-shopper gate that stops a double-click racing itself inside one process; a read of the shopper's
/// existing subscriptions before writing; and a deterministic <c>reference</c> on every record, which
/// Advanced Billing enforces as unique. If the first two are outrunning by a concurrent instance, the
/// third turns the duplicate into a 422 that this service resolves by returning the winner's subscription.
/// </para>
/// </remarks>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PlanCatalogCacheKey = "maxio:plan-catalog";
    private const int ProductPageSize = 200;
    private const int MaxProductPages = 25;

    private readonly AdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly MaxioReferenceFactory _references;
    private readonly SubscriberGate _gate;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        AdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        MaxioReferenceFactory references,
        SubscriberGate gate,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _references = references;
        _gate = gate;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.CatalogCacheDuration <= TimeSpan.Zero)
        {
            return await LoadPlansAsync(cancellationToken).ConfigureAwait(false);
        }

        var cached = await _cache.GetOrCreateAsync(PlanCatalogCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _settings.CatalogCacheDuration;
            return await LoadPlansAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return cached ?? Array.Empty<SubscriptionPlan>();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        Subscriber subscriber,
        string? planHandle,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var plan = await ResolvePlanAsync(planHandle, cancellationToken).ConfigureAwait(false);

        // Serialise this shopper's enrolments so the common double-click resolves with a single write.
        using var _ = await _gate.AcquireAsync(subscriber.Email, cancellationToken).ConfigureAwait(false);

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken).ConfigureAwait(false);
        var customerId = RequireCustomerId(customer);
        var existing = await ListRawSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);

        var decision = DecideReplay(existing, plan, idempotencyKey, subscriber.Email);

        if (decision.AlreadyOnFile is { } alreadyOnFile)
        {
            _logger.LogInformation(
                "Subscribe for customer {CustomerId} to plan {PlanHandle} matched existing subscription {SubscriptionId}; no new enrolment created.",
                customerId,
                plan.Handle,
                alreadyOnFile.Id);

            return new SubscribeResult(MapSubscription(alreadyOnFile, customerId), AlreadyExisted: true);
        }

        var created = await CreateSubscriptionAsync(customerId, plan, decision.Reference, cancellationToken)
            .ConfigureAwait(false);

        if (created.AlreadyExisted)
        {
            return created;
        }

        _logger.LogInformation(
            "Created Advanced Billing subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
            created.Subscription.Id,
            customerId,
            plan.Handle);

        return created;
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var reference = _references.ForCustomer(subscriber.Email);
        var customer = await ReadCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);

        if (customer is null)
        {
            // Never subscribed, so there is nothing to report — not an error.
            return Array.Empty<CustomerSubscription>();
        }

        var customerId = RequireCustomerId(customer);
        var subscriptions = await ListRawSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(s => s.Id ?? 0)
            .Select(s => MapSubscription(s, customerId))
            .ToList();
    }

    // ---------------------------------------------------------------- catalog

    private async Task<IReadOnlyList<SubscriptionPlan>> LoadPlansAsync(CancellationToken cancellationToken)
    {
        var currency = await ReadSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);
        var products = await ListProductsAsync(cancellationToken).ConfigureAwait(false);

        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p, currency))
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<Product>();

        for (var page = 1; page <= MaxProductPages; page++)
        {
            var input = new ListProductsForProductFamilyInput
            {
                // Advanced Billing resolves a product family by numeric id, or by handle when it is
                // prefixed like this. Handles are stable across catalog re-seeds; ids are not.
                ProductFamilyId = $"handle:{_settings.ProductFamilyHandle}",
                Page = page,
                PerPage = ProductPageSize,
                IncludeArchived = false,
            };

            var batch = await InvokeAsync(
                ct => _client.ProductFamiliesController.ListProductsForProductFamilyAsync(input, ct),
                $"listing plans in product family '{_settings.ProductFamilyHandle}'",
                cancellationToken).ConfigureAwait(false);

            if (batch is null || batch.Count == 0)
            {
                break;
            }

            products.AddRange(batch.Select(p => p.Product).Where(p => p is not null));

            if (batch.Count < ProductPageSize)
            {
                break;
            }
        }

        return products;
    }

    private async Task<string?> ReadSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        // Products carry a price in cents but no currency; the site's currency is where that lives.
        var site = await InvokeAsync(
            ct => _client.SitesController.ReadSiteAsync(ct),
            "reading the billing site",
            cancellationToken).ConfigureAwait(false);

        return site?.Site?.Currency;
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? planHandle, CancellationToken cancellationToken)
    {
        var requested = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle.Trim();

        if (string.IsNullOrWhiteSpace(requested))
        {
            throw new SubscriptionBillingRejectedException(
                "No plan was requested and no default plan is configured. " +
                $"Name a plan in the request, or set {MaxioSettings.SectionName}:DefaultPlanHandle.");
        }

        var plans = await ListPlansAsync(cancellationToken).ConfigureAwait(false);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, requested, StringComparison.OrdinalIgnoreCase));

        // Only offer plans from the configured product family, so a handle from elsewhere on the site
        // cannot be used to subscribe to something eShopOnWeb does not sell.
        return plan ?? throw new SubscriptionPlanNotFoundException(requested!);
    }

    // --------------------------------------------------------------- customer

    /// <summary>
    /// Returns the billing customer standing for <paramref name="subscriber"/>, creating it on first use.
    /// </summary>
    private async Task<Customer> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var reference = _references.ForCustomer(subscriber.Email);
        var existing = await ReadCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = MaxioCustomerNames.Resolve(subscriber);

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = reference,
            },
        };

        try
        {
            var created = await InvokeAsync(
                ct => _client.CustomersController.CreateCustomerAsync(request, ct),
                "creating the billing customer",
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created Advanced Billing customer {CustomerId} for reference {CustomerReference}.",
                created?.Customer?.Id,
                reference);

            return created?.Customer
                ?? throw new SubscriptionBillingUnavailableException(
                    "Advanced Billing accepted the customer but returned no customer record.");
        }
        catch (SubscriptionBillingRejectedException)
        {
            // Advanced Billing enforces reference uniqueness, so the most likely rejection here is that
            // a concurrent request created this customer between our read and our write. Re-read before
            // deciding it was really our request that was wrong.
            var raced = await ReadCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);

            if (raced is null)
            {
                throw;
            }

            _logger.LogInformation(
                "Billing customer {CustomerReference} was created concurrently; reusing customer {CustomerId}.",
                reference,
                raced.Id);

            return raced;
        }
    }

    /// <summary>
    /// Reads the customer with <paramref name="reference"/>, or <c>null</c> when no such customer exists.
    /// </summary>
    /// <remarks>
    /// A 404 here is an expected answer rather than a failure, so this call is made directly instead of
    /// through <see cref="InvokeAsync"/>, which would translate it into an exception.
    /// </remarks>
    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.CustomersController
                .ReadCustomerByReferenceAsync(reference, cancellationToken)
                .ConfigureAwait(false);

            return response?.Customer;
        }
        catch (ApiException ex) when (MaxioErrors.StatusCodeOf(ex) == 404)
        {
            // No customer with that reference yet: the shopper has simply never subscribed.
            return null;
        }
        catch (ApiException ex)
        {
            throw MaxioErrors.Translate(ex, "looking up the billing customer");
        }
        catch (Exception ex) when (MaxioErrors.IsTransport(ex) && !cancellationToken.IsCancellationRequested)
        {
            throw MaxioErrors.TranslateTransport(ex, "looking up the billing customer");
        }
    }

    private static int RequireCustomerId(Customer customer) =>
        customer.Id ?? throw new SubscriptionBillingUnavailableException(
            "Advanced Billing returned a customer without an id.");

    // ----------------------------------------------------------- subscription

    private async Task<List<Subscription>> ListRawSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await InvokeAsync(
            ct => _client.CustomersController.ListCustomerSubscriptionsAsync(customerId, ct),
            "listing the shopper's subscriptions",
            cancellationToken).ConfigureAwait(false);

        return response?
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .ToList() ?? new List<Subscription>();
    }

    /// <summary>
    /// Whether this subscribe request has already been satisfied, and what reference a new subscription
    /// should carry if it has not.
    /// </summary>
    /// <param name="AlreadyOnFile">The subscription that already covers this intent, if there is one.</param>
    /// <param name="Reference">The reference to create the new subscription under.</param>
    private readonly record struct ReplayDecision(Subscription? AlreadyOnFile, string Reference);

    private ReplayDecision DecideReplay(
        IReadOnlyList<Subscription> existing,
        SubscriptionPlan plan,
        string? idempotencyKey,
        string email)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // The caller told us what counts as "the same request"; honour exactly that.
            var keyed = _references.ForSubscription(email, idempotencyKey);
            return new ReplayDecision(FindByReference(existing, keyed), keyed);
        }

        var forThisPlan = existing
            .Where(s => string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var live = forThisPlan.FirstOrDefault(s => MaxioEnums.IsLive(s.State));

        if (live is not null)
        {
            return new ReplayDecision(live, live.Reference ?? string.Empty);
        }

        // Nothing live on this plan, so this is a genuine enrolment. Number it past the shopper's previous
        // runs at the same plan: identical for a replayed click, distinct from a deliberate re-subscribe
        // after cancelling.
        var sequence = forThisPlan.Count + 1;
        var reference = _references.ForSubscription(email, plan.Handle, sequence);

        // Subscriptions the shopper cancelled and re-took can leave gaps, so walk past anything already used.
        while (FindByReference(existing, reference) is not null)
        {
            sequence++;
            reference = _references.ForSubscription(email, plan.Handle, sequence);
        }

        return new ReplayDecision(null, reference);
    }

    private static Subscription? FindByReference(IEnumerable<Subscription> subscriptions, string reference) =>
        subscriptions.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal));

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        int customerId,
        SubscriptionPlan plan,
        string reference,
        CancellationToken cancellationToken)
    {
        if (!MaxioCollectionMethods.TryParse(_settings.PaymentCollectionMethod, out var collectionMethod))
        {
            throw new SubscriptionBillingConfigurationException(
                $"{MaxioSettings.SectionName}:PaymentCollectionMethod '{_settings.PaymentCollectionMethod}' is not " +
                $"a collection method Advanced Billing accepts. Use one of: {MaxioCollectionMethods.SupportedList}.");
        }

        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customerId,
                PaymentCollectionMethod = collectionMethod,
                Reference = reference,
            },
        };

        try
        {
            var response = await InvokeAsync(
                ct => _client.SubscriptionsController.CreateSubscriptionAsync(request, ct),
                $"subscribing customer {customerId} to plan '{plan.Handle}'",
                cancellationToken).ConfigureAwait(false);

            var subscription = response?.Subscription
                ?? throw new SubscriptionBillingUnavailableException(
                    "Advanced Billing accepted the subscription but returned no subscription record.");

            return new SubscribeResult(MapSubscription(subscription, customerId), AlreadyExisted: false);
        }
        catch (SubscriptionBillingRejectedException)
        {
            // A rejection here is ambiguous: it could be a genuinely invalid request (a plan that demands
            // a payment method, say), or it could be the unique-reference guard firing because another
            // instance enrolled this shopper first. Rather than pattern-match on Advanced Billing's error
            // prose, ask it what exists now — the answer is authoritative either way.
            var refreshed = await ListRawSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);
            var raced = FindByReference(refreshed, reference);

            if (raced is null)
            {
                throw;
            }

            _logger.LogInformation(
                "Subscription {Reference} was created concurrently; returning existing subscription {SubscriptionId}.",
                reference,
                raced.Id);

            return new SubscribeResult(MapSubscription(raced, customerId), AlreadyExisted: true);
        }
    }

    // ---------------------------------------------------------------- mapping

    private static SubscriptionPlan MapPlan(Product product, string? currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
        Price = FromCents(product.PriceInCents) ?? 0m,
        Currency = currency,
        IntervalLength = product.Interval,
        IntervalUnit = MaxioEnums.ToWireValueOrNull(product.IntervalUnit),
        RequiresPaymentMethod = product.RequireCreditCard ?? false,
    };

    private static CustomerSubscription MapSubscription(Subscription subscription, int customerId) => new()
    {
        Id = subscription.Id ?? 0,
        Reference = subscription.Reference,
        State = MaxioEnums.ToWireValueOrNull(subscription.State) ?? "unknown",
        IsLive = MaxioEnums.IsLive(subscription.State),
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,

        // The subscription's own price is what the shopper actually pays: it is pinned to the product
        // version they signed up on, which can differ from the plan's price in the catalog today.
        Price = FromCents(subscription.ProductPriceInCents) ?? FromCents(subscription.Product?.PriceInCents),
        Currency = subscription.Currency,
        IntervalLength = subscription.Product?.Interval,
        IntervalUnit = MaxioEnums.ToWireValueOrNull(subscription.Product?.IntervalUnit),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,

        // next_assessment_at is when Advanced Billing will actually try to bill; it tracks the period end
        // except when a payment failed and a retry is pending, which is precisely when they differ and the
        // retry date is the one the shopper cares about.
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        PaymentCollectionMethod = MaxioEnums.ToWireValueOrNull(subscription.PaymentCollectionMethod),
        BillingCustomerId = subscription.Customer?.Id ?? customerId,
    };

    private static decimal? FromCents(long? cents) =>
        cents is null ? null : cents.Value / 100m;

    // ------------------------------------------------------------- invocation

    /// <summary>
    /// Runs an SDK call, translating every way it can fail into a domain billing exception.
    /// </summary>
    private async Task<T> InvokeAsync<T>(
        Func<CancellationToken, Task<T>> call,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "Advanced Billing returned HTTP {StatusCode} while {Operation}.",
                MaxioErrors.StatusCodeOf(ex),
                operation);

            throw MaxioErrors.Translate(ex, operation);
        }
        catch (Exception ex) when (MaxioErrors.IsTransport(ex) && !cancellationToken.IsCancellationRequested)
        {
            throw MaxioErrors.TranslateTransport(ex, operation);
        }
    }
}
