using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Implements subscription billing against Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// <para>
/// Maxio is the system of record: this service holds no subscription state of its own and reads
/// everything back from Maxio, so the integration behaves correctly even on the in-memory database,
/// where nothing survives a restart.
/// </para>
/// <para>
/// Users are tied to Maxio through a deterministic reference derived from their eShopOnWeb user name
/// (see <see cref="MaxioReferences"/>). That, plus the uniqueness Maxio enforces on references, is what
/// makes signup idempotent: a repeated attempt either finds the live subscription and returns it, or
/// loses the create race and is told the reference is taken, at which point it re-reads the winner.
/// </para>
/// </remarks>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Maxio quotes money in the minor unit of the site currency -- the wire fields are literally named
    /// <c>price_in_cents</c> -- so amounts are scaled by 100 for display.
    /// </summary>
    private const decimal MinorUnitsPerUnit = 100m;

    /// <summary>Fragment of the Maxio 422 message returned when a product handle is not on the site.</summary>
    private const string UnknownProductHandleMarker = "Product with API Handle";

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly AsyncTtlCache<MaxioPlanCatalog> _catalogCache;
    private readonly KeyedAsyncLock _subscribeLock;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        AsyncTtlCache<MaxioPlanCatalog> catalogCache,
        KeyedAsyncLock subscribeLock,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _catalogCache = catalogCache;
        _subscribeLock = subscribeLock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog.Plans;
    }

    /// <inheritdoc />
    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            throw new ArgumentException("A user name is required to subscribe.", nameof(request));
        }

        var catalog = await GetCatalogAsync(cancellationToken);
        var plan = ResolvePlan(catalog, request.PlanHandle);
        var customerReference = MaxioReferences.CustomerReference(_settings.ReferencePrefix, request.UserName);

        // Serialise this user's concurrent attempts so the second one observes the first one's work
        // rather than racing it through the check-then-create below.
        using var _ = await _subscribeLock.AcquireAsync(customerReference, cancellationToken);

        var customer = await EnsureCustomerAsync(request.UserName, customerReference, cancellationToken);
        var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        var alreadyHeld = existingSubscriptions.FirstOrDefault(s => IsLiveSubscriptionTo(s, plan.Handle));
        if (alreadyHeld is not null)
        {
            _logger.LogInformation(
                "User {UserName} already holds live Maxio subscription {SubscriptionId} to plan {PlanHandle}; not creating another.",
                request.UserName,
                alreadyHeld.Id,
                plan.Handle);

            return new SubscribeResult(ToCustomerSubscription(alreadyHeld), AlreadySubscribed: true);
        }

        var referenceRoot = MaxioReferences.SubscriptionReferenceRoot(customerReference, plan.Handle);
        var reference = MaxioReferences.NextAvailableSubscriptionReference(
            referenceRoot,
            existingSubscriptions.Select(s => s.Reference));

        try
        {
            var created = await _client.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = reference,
                    PaymentCollectionMethod = _settings.PaymentCollectionMethod
                },
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({Reference}) to plan {PlanHandle} for user {UserName}; state {State}.",
                created.Id,
                created.Reference,
                plan.Handle,
                request.UserName,
                created.State);

            return new SubscribeResult(ToCustomerSubscription(created), AlreadySubscribed: false);
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            // Another instance created this subscription between our read and our write. Maxio rejected
            // the duplicate, which is exactly the outcome we want: adopt the winner instead of retrying.
            _logger.LogInformation(
                "Maxio reported subscription reference {Reference} as taken for user {UserName}; adopting the existing subscription.",
                reference,
                request.UserName);

            var winner = await FindSubscriptionAfterRaceAsync(customer.Id, reference, plan.Handle, cancellationToken);
            if (winner is not null)
            {
                return new SubscribeResult(ToCustomerSubscription(winner), AlreadySubscribed: true);
            }

            throw;
        }
        catch (MaxioApiException ex) when (IsUnknownProductHandle(ex))
        {
            // The plan vanished between the cached catalog and this call. Drop the stale snapshot so the
            // next caller sees reality, and report it as a missing plan rather than an upstream fault.
            _catalogCache.Invalidate();
            throw new SubscriptionPlanNotFoundException(plan.Handle, DescribeAvailablePlans(catalog.Plans));
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required to list subscriptions.", nameof(userName));
        }

        var customerReference = MaxioReferences.CustomerReference(_settings.ReferencePrefix, userName);
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);

        if (customer is null)
        {
            // The user has never subscribed, so no Maxio customer exists yet. That is not an error.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(ToCustomerSubscription)
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// Finds the Maxio customer for a user, creating one on first use.
    /// </summary>
    /// <remarks>
    /// Look up first, create second, and treat a rejected duplicate reference as "somebody else created
    /// it" rather than as a failure. That keeps the operation idempotent both for a double-clicking
    /// shopper and for two application instances handling their requests at the same time.
    /// </remarks>
    private async Task<MaxioCustomer> EnsureCustomerAsync(string userName, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = MaxioReferences.DeriveCustomerName(userName);

        try
        {
            var created = await _client.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = userName,
                    Reference = customerReference
                },
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} ({Reference}) for user {UserName}.",
                created.Id,
                created.Reference,
                userName);

            return created;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            var winner = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    /// <summary>
    /// Re-reads the customer after losing a create race, preferring the subscription that took our
    /// reference and falling back to any live subscription to the same plan.
    /// </summary>
    private async Task<MaxioSubscription?> FindSubscriptionAfterRaceAsync(
        long customerId,
        string reference,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.OrdinalIgnoreCase))
               ?? subscriptions.FirstOrDefault(s => IsLiveSubscriptionTo(s, planHandle));
    }

    private Task<MaxioPlanCatalog> GetCatalogAsync(CancellationToken cancellationToken) =>
        _catalogCache.GetAsync(LoadCatalogAsync, cancellationToken);

    private async Task<MaxioPlanCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}' is required to list subscription plans.");
        }

        // Resolve the family by handle every time the catalog is refreshed: Maxio reassigns numeric ids
        // when a site is re-seeded, so caching an id across deployments would silently point at nothing.
        var family = await _client.FindProductFamilyByHandleAsync(familyHandle, cancellationToken);
        if (family is null)
        {
            throw new BillingConfigurationException(
                $"Maxio product family '{familyHandle}' was not found on the configured site. " +
                $"Check '{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}'.");
        }

        var site = await _client.ReadSiteAsync(cancellationToken);
        var products = await _client.ListProductsForFamilyAsync(family.Id, cancellationToken);
        var currency = site.Currency ?? string.Empty;

        var plans = products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => ToSubscriptionPlan(p, currency))
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Loaded {PlanCount} Maxio plan(s) from product family {FamilyHandle} (id {FamilyId}) on site {Subdomain}.",
            plans.Count,
            familyHandle,
            family.Id,
            site.Subdomain);

        return new MaxioPlanCatalog(family.Id, currency, plans);
    }

    /// <summary>
    /// Picks the plan a subscribe request targets: the requested handle, else the configured default,
    /// else the only plan on offer when the family has exactly one.
    /// </summary>
    /// <remarks>
    /// No plan handle is baked into the build. A deployment that offers several plans and configures no
    /// default gets a clear error naming the handles it can choose from, rather than an arbitrary pick.
    /// </remarks>
    private SubscriptionPlan ResolvePlan(MaxioPlanCatalog catalog, string? requestedHandle)
    {
        var handle = FirstNonBlank(requestedHandle, _settings.DefaultPlanHandle);

        if (handle is null)
        {
            if (catalog.Plans.Count == 1)
            {
                return catalog.Plans[0];
            }

            throw new SubscriptionPlanNotFoundException("(none specified)", DescribeAvailablePlans(catalog.Plans));
        }

        return catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase))
               ?? throw new SubscriptionPlanNotFoundException(handle, DescribeAvailablePlans(catalog.Plans));
    }

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    private static string DescribeAvailablePlans(IReadOnlyList<SubscriptionPlan> plans) =>
        plans.Count == 0 ? "(none)" : string.Join(", ", plans.Select(p => p.Handle));

    private static bool IsLiveSubscriptionTo(MaxioSubscription subscription, string planHandle) =>
        string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
        SubscriptionStates.IsLive(subscription.State);

    private static bool IsUnknownProductHandle(MaxioApiException exception) =>
        exception.StatusCode == HttpStatusCode.UnprocessableEntity &&
        exception.Errors.Any(e => e.Contains(UnknownProductHandleMarker, StringComparison.OrdinalIgnoreCase));

    private static SubscriptionPlan ToSubscriptionPlan(MaxioProduct product, string currency) =>
        new(
            Handle: product.Handle!,
            Name: product.Name ?? product.Handle!,
            Description: product.Description,
            Price: product.PriceInCents / MinorUnitsPerUnit,
            Currency: currency,
            Interval: product.Interval,
            IntervalUnit: product.IntervalUnit ?? string.Empty,
            TrialInterval: product.TrialInterval,
            TrialIntervalUnit: product.TrialInterval is null ? null : product.TrialIntervalUnit,
            RequiresPaymentMethod: product.RequireCreditCard);

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) =>
        new(
            Id: subscription.Id,
            Reference: subscription.Reference,
            State: subscription.State ?? string.Empty,
            PlanHandle: subscription.Product?.Handle ?? string.Empty,
            PlanName: subscription.Product?.Name ?? string.Empty,
            Price: subscription.ProductPriceInCents / MinorUnitsPerUnit,
            Currency: subscription.Currency ?? string.Empty,
            CurrentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            NextBillingAt: subscription.NextAssessmentAt,
            CanceledAt: subscription.CanceledAt,
            CreatedAt: subscription.CreatedAt,
            PaymentCollectionMethod: subscription.PaymentCollectionMethod);
}
