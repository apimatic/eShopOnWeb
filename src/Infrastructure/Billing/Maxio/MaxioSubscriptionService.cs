using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// <para>
/// Maxio is the system of record: plans are the products of the configured product family, the
/// shopper's billing customer is found by a reference derived from their eShopOnWeb user name, and
/// the subscriptions they hold are read back from Maxio rather than mirrored locally. Nothing about
/// a shopper's billing state is stored in the eShopOnWeb database, so it survives a restart.
/// </para>
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Namespace for the customer <c>reference</c>, so eShopOnWeb customers stay recognisable on a
    /// Maxio site that other systems may also write to.
    /// </summary>
    private const string ReferenceNamespace = "eshoponweb";

    /// <summary>Used when no name can be derived for a shopper; Maxio requires a last name.</summary>
    private const string FallbackLastName = "Customer";

    private const int PlansPerPage = 200;
    private const int MaxPlanPages = 10;

    /// <summary>
    /// Values of the specification's <c>Collection-Method</c> enumeration. eShopOnWeb does not
    /// capture card details, so a shopper is invoiced unless the plan insists on a payment method,
    /// in which case Maxio is left to charge whatever profile the customer already has.
    /// </summary>
    private const string CollectionMethodAutomatic = "automatic";
    private const string CollectionMethodRemittance = "remittance";
    private const string CollectionMethodInvoice = "invoice";

    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SiteCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly char[] NameSeparators = { '.', '_', '-', '+' };

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscriberLock;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioApiClient client, IOptionsMonitor<MaxioOptions> options,
        IMemoryCache cache, KeyedAsyncLock subscriberLock, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _cache = cache;
        _subscriberLock = subscriberLock;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        var products = await GetPublishedProductsAsync(cancellationToken);
        var site = await GetSiteAsync(cancellationToken);

        return products
            .Select(product => MapPlan(product, site?.Currency))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriber.UserName);

        var products = await GetPublishedProductsAsync(cancellationToken);
        var product = ResolvePlan(products, planHandle);
        var customerReference = BuildCustomerReference(subscriber.UserName);
        var site = await GetSiteAsync(cancellationToken);

        // Everything below reads Maxio and then decides whether to write, so it has to run once at
        // a time per shopper.
        using var _ = await _subscriberLock.AcquireAsync(customerReference, cancellationToken);

        var customer = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken);
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        var existing = subscriptions
            .Where(subscription => IsForProduct(subscription, product))
            .Where(subscription => !ParseState(subscription.State).IsTerminal())
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        if (existing is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerId} already holds subscription {SubscriptionId} to plan {PlanHandle}; returning it unchanged.",
                customer.Id, existing.Id, product.Handle);

            return new SubscribeResult(MapSubscription(existing, site?.Currency), AlreadySubscribed: true);
        }

        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = product.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = ResolvePaymentCollectionMethod(product, site),
                Reference = BuildSubscriptionReference(customerReference, product.Handle!, subscriptions)
            }
        };

        var created = await _client.CreateSubscriptionAsync(request, cancellationToken);

        _logger.LogInformation(
            "Created subscription {SubscriptionId} to plan {PlanHandle} for customer {CustomerId} in state {State}.",
            created.Id, product.Handle, customer.Id, created.State);

        return new SubscribeResult(MapSubscription(created, site?.Currency), AlreadySubscribed: false);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriber.UserName);

        var customerReference = BuildCustomerReference(subscriber.UserName);
        var customer = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken);

        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var site = await GetSiteAsync(cancellationToken);
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(subscription => MapSubscription(subscription, site?.Currency))
            .ToList();
    }

    /// <summary>
    /// Finds the shopper's billing customer by reference and creates it on first use. Creating is
    /// safe to repeat: Maxio allows only one customer per reference, so a lost race is resolved by
    /// looking the winner up again.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber,
        string customerReference, CancellationToken cancellationToken)
    {
        var existing = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(subscriber);

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email ?? subscriber.UserName,
                Organization = string.IsNullOrWhiteSpace(subscriber.Organization)
                    ? null
                    : subscriber.Organization.Trim(),
                Reference = customerReference
            }
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id, customerReference);

            return created;
        }
        catch (BillingValidationException ex)
        {
            // The most likely rejection here is the reference already being taken, which means a
            // concurrent request created the customer between the lookup and this call.
            var raced = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken);

            if (raced is null)
            {
                throw;
            }

            _logger.LogInformation(ex,
                "Maxio customer {CustomerId} for reference {CustomerReference} already existed; reusing it.",
                raced.Id, customerReference);

            return raced;
        }
    }

    /// <summary>
    /// The products of the configured product family, which are the plans eShopOnWeb publishes.
    /// Archived products are dropped, and the result is cached briefly so browsing plans and
    /// subscribing do not hit Maxio for the same catalogue on every request.
    /// </summary>
    private async Task<IReadOnlyList<MaxioProduct>> GetPublishedProductsAsync(
        CancellationToken cancellationToken)
    {
        var options = GetValidatedOptions();
        var cacheKey = $"maxio:plans:{options.ResolveBaseUrl()}:{options.ResolveProductFamilyPathValue()}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<MaxioProduct>? cached) && cached is not null)
        {
            return cached;
        }

        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var batch = await _client.ListProductsForProductFamilyAsync(
                options.ResolveProductFamilyPathValue(), page, PlansPerPage, cancellationToken);

            products.AddRange(batch.Where(product =>
                product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle)));

            if (batch.Count < PlansPerPage)
            {
                break;
            }
        }

        _cache.Set(cacheKey, (IReadOnlyList<MaxioProduct>)products, PlanCacheDuration);

        return products;
    }

    /// <summary>
    /// The billing site, which supplies the currency prices are quoted in and the invoicing
    /// architecture that decides which collection methods are accepted. A failure to read it must
    /// not stop a shopper from browsing or subscribing, so the defaults are used in that case.
    /// </summary>
    private async Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken)
    {
        var options = GetValidatedOptions();
        var cacheKey = $"maxio:site:{options.ResolveBaseUrl()}";

        if (_cache.TryGetValue(cacheKey, out MaxioSite? cached))
        {
            return cached;
        }

        MaxioSite? site = null;

        try
        {
            site = await _client.ReadSiteAsync(cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(ex, "Could not read the Maxio site; falling back to defaults for currency and collection method.");
        }

        _cache.Set(cacheKey, site, SiteCacheDuration);

        return site;
    }

    /// <summary>
    /// Picks the collection method for a new subscription.
    /// <para>
    /// eShopOnWeb subscribes shoppers without collecting card details, so an automatic charge at
    /// signup would simply fail for want of a payment method. The shopper is invoiced instead -
    /// <c>remittance</c> on Relationship Invoicing sites and <c>invoice</c> on legacy Statements
    /// sites, as the specification's <c>Collection-Method</c> enumeration describes. A plan that
    /// insists on a payment method is left on <c>automatic</c>, so Maxio charges the profile the
    /// customer already has, and says so plainly when there is none.
    /// </para>
    /// </summary>
    private static string ResolvePaymentCollectionMethod(MaxioProduct product, MaxioSite? site)
    {
        if (product.RequireCreditCard)
        {
            return CollectionMethodAutomatic;
        }

        // Without a site to ask, assume the current architecture.
        return site is null || site.RelationshipInvoicingEnabled
            ? CollectionMethodRemittance
            : CollectionMethodInvoice;
    }

    private MaxioOptions GetValidatedOptions()
    {
        var options = _options.CurrentValue;
        var errors = options.Validate();

        if (errors.Count > 0)
        {
            throw new BillingConfigurationException(
                "Maxio subscription billing is not configured: " + string.Join(" ", errors));
        }

        return options;
    }

    private static MaxioProduct ResolvePlan(IReadOnlyList<MaxioProduct> products, string? planHandle)
    {
        var handles = products.Select(product => product.Handle!).ToList();

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            // With a single published plan there is nothing to choose, so it is the default target.
            if (products.Count == 1)
            {
                return products[0];
            }

            throw new BillingValidationException(
                "A plan handle is required when more than one plan is published. Available plans: " +
                $"{SubscriptionPlanNotFoundException.Describe(handles)}.");
        }

        var requested = planHandle.Trim();
        var match = products.FirstOrDefault(product =>
            string.Equals(product.Handle, requested, StringComparison.OrdinalIgnoreCase));

        return match ?? throw SubscriptionPlanNotFoundException.ForHandle(requested, handles);
    }

    private static bool IsForProduct(MaxioSubscription subscription, MaxioProduct product) =>
        subscription.Product is not null &&
        (subscription.Product.Id == product.Id ||
         string.Equals(subscription.Product.Handle, product.Handle, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The shopper's stable identity at Maxio. The same eShopOnWeb user always resolves to the same
    /// customer, which is what makes creating one idempotent.
    /// </summary>
    private static string BuildCustomerReference(string userName) =>
        $"{ReferenceNamespace}:{userName.Trim().ToLowerInvariant()}";

    /// <summary>
    /// A readable reference for the subscription. It is deterministic per shopper and plan, so a
    /// replay is recognisable in Maxio; if the shopper previously held - and ended - a subscription
    /// to the same plan, the new one gets a distinct suffix rather than colliding with it.
    /// </summary>
    private static string BuildSubscriptionReference(string customerReference, string planHandle,
        IReadOnlyList<MaxioSubscription> existingSubscriptions)
    {
        var reference = $"{customerReference}:{planHandle}";

        var taken = existingSubscriptions.Any(subscription =>
            string.Equals(subscription.Reference, reference, StringComparison.OrdinalIgnoreCase));

        return taken
            ? $"{reference}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}"
            : reference;
    }

    /// <summary>
    /// Maxio requires a first and last name for a customer. eShopOnWeb identities carry neither, so
    /// the caller may supply them and otherwise they are derived from the email address.
    /// </summary>
    private static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();

        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
        {
            return (first, last);
        }

        var identifier = subscriber.Email ?? subscriber.UserName;
        var localPart = identifier.Split('@')[0];
        var parts = localPart
            .Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .Where(part => part.Length > 0)
            .ToList();

        var derivedFirst = parts.Count > 0 ? parts[0] : Capitalize(subscriber.UserName);
        var derivedLast = parts.Count > 1 ? string.Join(" ", parts.Skip(1)) : FallbackLastName;

        return (string.IsNullOrEmpty(first) ? derivedFirst : first,
            string.IsNullOrEmpty(last) ? derivedLast : last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        return char.ToUpper(trimmed[0], CultureInfo.InvariantCulture) + trimmed.Substring(1);
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product, string? currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, string? currency) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        CustomerEmail = subscription.Customer?.Email,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        State = ParseState(subscription.State),
        StateName = subscription.State ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency ?? currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        // next_assessment_at tracks the period end but diverges after a failed payment, so it is
        // the honest answer to "when am I billed next?".
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt
    };

    /// <summary>
    /// Maps the specification's <c>Subscription-State</c> values onto the application's enumeration.
    /// An unrecognised value maps to <see cref="SubscriptionState.Unknown"/> rather than failing,
    /// and the raw value is reported alongside it.
    /// </summary>
    private static SubscriptionState ParseState(string? state) => state switch
    {
        "pending" => SubscriptionState.Pending,
        "failed_to_create" => SubscriptionState.FailedToCreate,
        "trialing" => SubscriptionState.Trialing,
        "assessing" => SubscriptionState.Assessing,
        "active" => SubscriptionState.Active,
        "soft_failure" => SubscriptionState.SoftFailure,
        "past_due" => SubscriptionState.PastDue,
        "suspended" => SubscriptionState.Suspended,
        "canceled" => SubscriptionState.Canceled,
        "expired" => SubscriptionState.Expired,
        "paused" => SubscriptionState.Paused,
        "unpaid" => SubscriptionState.Unpaid,
        "trial_ended" => SubscriptionState.TrialEnded,
        "on_hold" => SubscriptionState.OnHold,
        "awaiting_signup" => SubscriptionState.AwaitingSignup,
        _ => SubscriptionState.Unknown
    };
}
