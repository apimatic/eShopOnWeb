using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Recurring subscription billing backed by Maxio Advanced Billing.
///
/// Maxio is the system of record: eShopOnWeb stores no subscription state of its own, so every
/// answer is read back from Maxio and the integration stays correct across restarts and instances
/// (which matters because eShopOnWeb can run on an in-memory database).
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string PlanCacheKey = "maxio:subscription-plans";
    private const string SiteCacheKey = "maxio:site";
    private const int PlansPageSize = 200;   // spec caps per_page at 200
    private const int MaxPlanPages = 25;

    private static readonly StripedAsyncLock SubscribeLock = new();

    private readonly IMaxioApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IMemoryCache cache,
        IOptionsMonitor<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _cache = cache;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var settings = EnsureConfigured();

        if (_cache.TryGetValue(PlanCacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        return CachePlans(settings, await LoadPlansAsync(settings, cancellationToken));
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var settings = EnsureConfigured();
        var subscriber = request.Subscriber;

        var plan = await ResolvePlanAsync(request.PlanHandle, cancellationToken);
        var subscriptionReference = BuildSubscriptionReference(subscriber, request.IdempotencyKey);

        // Serialise concurrent attempts by the same shopper so a double-click cannot create two
        // customers or two subscriptions.
        using (await SubscribeLock.AcquireAsync(subscriber.Reference, cancellationToken))
        {
            if (subscriptionReference is not null)
            {
                var replay = await ExecuteAsync(
                    () => _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken),
                    "look up subscription by reference");

                if (replay is not null)
                {
                    _logger.LogInformation(
                        "Idempotent subscribe: subscription {SubscriptionId} already exists for reference {Reference}",
                        replay.Id, subscriptionReference);

                    return new SubscribeResult(MapSubscription(replay), created: false);
                }
            }

            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Idempotent subscribe: customer {CustomerId} already has {State} subscription {SubscriptionId} to plan {PlanHandle}",
                    customer.Id, existing.State, existing.Id, plan.Handle);

                return new SubscribeResult(MapSubscription(existing), created: false);
            }

            var payload = new CreateSubscription
            {
                ProductHandle = plan.Handle,
                ProductPricePointHandle = request.PricePointHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = await ResolveCollectionMethodAsync(plan, settings, cancellationToken),
                Reference = subscriptionReference
            };

            var created = await ExecuteAsync(
                () => _client.CreateSubscriptionAsync(payload, cancellationToken),
                $"create subscription to plan '{plan.Handle}'");

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {PlanHandle} (site {Subdomain})",
                created.Id, created.State, customer.Id, plan.Handle, settings.Subdomain);

            return new SubscribeResult(MapSubscription(created), created: true);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var customer = await ExecuteAsync(
            () => _client.ReadCustomerByReferenceAsync(subscriber.Reference, cancellationToken),
            "look up billing customer");

        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            "list customer subscriptions");

        return subscriptions
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(MapSubscription)
            .ToArray();
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> LoadPlansAsync(MaxioSettings settings, CancellationToken cancellationToken)
    {
        var familyHandle = settings.ProductFamilyHandle?.Trim();
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var currentPage = page;
            var batch = string.IsNullOrWhiteSpace(familyHandle)
                ? await ExecuteAsync(
                    () => _client.ListProductsAsync(currentPage, PlansPageSize, includeArchived: false, cancellationToken),
                    "list subscription plans")
                : await ExecuteAsync(
                    () => _client.ListProductsForProductFamilyAsync($"handle:{familyHandle}", currentPage, PlansPageSize, includeArchived: false, cancellationToken),
                    $"list subscription plans for product family '{familyHandle}'");

            products.AddRange(batch);

            if (batch.Count < PlansPageSize)
            {
                break;
            }
        }

        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapPlan)
            .ToArray();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetPlansAsync(cancellationToken);
        var plan = Match(plans, planHandle);

        if (plan is null)
        {
            // The catalog may have changed since the cached copy was taken - confirm against Maxio
            // before rejecting the request.
            var settings = EnsureConfigured();
            plans = await LoadPlansAsync(settings, cancellationToken);
            if (settings.PlanCacheSeconds > 0)
            {
                _cache.Set(PlanCacheKey, plans, TimeSpan.FromSeconds(settings.PlanCacheSeconds));
            }

            plan = Match(plans, planHandle);
        }

        if (plan is null)
        {
            _logger.LogWarning("Subscribe rejected: plan handle {PlanHandle} is not in the billing catalog", planHandle);
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        return plan;
    }

    private IReadOnlyList<SubscriptionPlan> CachePlans(MaxioSettings settings, IReadOnlyList<SubscriptionPlan> plans)
    {
        if (settings.ResolvePlanCacheDuration() is { } duration)
        {
            _cache.Set(PlanCacheKey, plans, duration);
        }

        return plans;
    }

    /// <summary>
    /// Chooses the payment collection method for a new subscription
    /// (maxio-spec components/schemas/Collection-Method.yaml).
    ///
    /// Plans that do not require a stored payment method are enrolled on invoice billing, so that
    /// Maxio issues an invoice instead of attempting a card capture that would necessarily fail for
    /// a shopper without a payment profile. Which invoice-billing value is valid depends on the
    /// site's architecture, which is why the site is read: "remittance" under Relationship
    /// Invoicing, "invoice" under legacy Statements. Plans that do require a payment method keep the
    /// site default, so Maxio enforces its own payment-profile rules.
    /// An explicit Maxio:PaymentCollectionMethod setting always wins.
    /// </summary>
    private async Task<string?> ResolveCollectionMethodAsync(SubscriptionPlan plan, MaxioSettings settings, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.PaymentCollectionMethod))
        {
            return settings.PaymentCollectionMethod!.Trim();
        }

        if (plan.RequiresPaymentMethod)
        {
            return null;
        }

        var site = await GetSiteAsync(cancellationToken);

        return site is null || site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
    }

    private async Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCacheKey, out MaxioSite? cached))
        {
            return cached;
        }

        try
        {
            var site = await _client.ReadSiteAsync(cancellationToken);
            _cache.Set(SiteCacheKey, site, TimeSpan.FromMinutes(15));

            return site;
        }
        catch (MaxioApiException ex)
        {
            // Not fatal: fall back to the current (Relationship Invoicing) architecture and let the
            // subscribe call itself report any problem.
            _logger.LogWarning(ex, "Unable to read Maxio site settings; assuming Relationship Invoicing");
            return null;
        }
    }

    private static SubscriptionPlan? Match(IReadOnlyList<SubscriptionPlan> plans, string planHandle) =>
        plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the Maxio customer for this shopper, creating it when it does not exist yet.
    /// The customer's "reference" is the shopper's stable key, which Maxio enforces as unique -
    /// so a lost race on create is resolved by reading the winner back.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var existing = await ExecuteAsync(
            () => _client.ReadCustomerByReferenceAsync(subscriber.Reference, cancellationToken),
            "look up billing customer");

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(subscriber);
        var payload = new CreateCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = subscriber.Email,
            Reference = subscriber.Reference
        };

        try
        {
            var created = await ExecuteAsync(
                () => _client.CreateCustomerAsync(payload, cancellationToken),
                "create billing customer");

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}", created.Id, subscriber.Reference);
            return created;
        }
        catch (BillingRequestInvalidException)
        {
            // A concurrent caller (another instance, or a retry after an ambiguous failure) may have
            // created the customer between the lookup and the create; the unique reference makes the
            // duplicate observable, so read the winner back before giving up.
            var winner = await ExecuteAsync(
                () => _client.ReadCustomerByReferenceAsync(subscriber.Reference, cancellationToken),
                "look up billing customer");

            if (winner is not null)
            {
                _logger.LogInformation("Reusing Maxio customer {CustomerId} created concurrently for reference {Reference}",
                    winner.Id, subscriber.Reference);
                return winner;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken),
            "list customer subscriptions");

        return subscriptions.FirstOrDefault(subscription =>
            SubscriptionStates.IsLive(subscription.State) &&
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Deterministic subscription reference for a caller supplied idempotency key. Keys are hashed
    /// so that arbitrary caller input cannot inject separators or unbounded length into the value
    /// stored on the billing record, and are scoped to the shopper so keys cannot collide between users.
    /// </summary>
    private static string? BuildSubscriptionReference(Subscriber subscriber, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var material = $"{subscriber.Reference}|{idempotencyKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));

        return "eshoponweb-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static (string FirstName, string LastName) ResolveName(Subscriber subscriber)
    {
        // Maxio requires first_name and last_name on customer creation
        // (maxio-spec components/schemas/Create-Customer.yaml). eShopOnWeb identities only carry an
        // e-mail address, so derive a reasonable pair from it when nothing better is available.
        var firstName = subscriber.FirstName?.Trim();
        var lastName = subscriber.LastName?.Trim();

        if (!string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(lastName))
        {
            return (firstName, lastName);
        }

        var localPart = subscriber.Email.Split('@')[0];
        var tokens = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .ToArray();

        firstName = string.IsNullOrEmpty(firstName)
            ? (tokens.Length > 0 ? tokens[0] : "eShopOnWeb")
            : firstName;

        lastName = string.IsNullOrEmpty(lastName)
            ? (tokens.Length > 1 ? string.Join(' ', tokens.Skip(1)) : "Customer")
            : lastName;

        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        PricePointName = product.ProductPricePointName,
        PricePointHandle = product.ProductPricePointHandle,
        RequiresPaymentMethod = product.RequireCreditCard,
        Taxable = product.Taxable,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents,
        SetupFeeInCents = product.InitialChargeInCents,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        ProductFamilyName = product.ProductFamily?.Name,
        ProviderPlanId = product.Id
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PlanPriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        PricePointName = subscription.Product?.ProductPricePointName,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        TrialEndedAt = subscription.TrialEndedAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        Reference = subscription.Reference,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        CustomerEmail = subscription.Customer?.Email
    };

    private MaxioSettings EnsureConfigured()
    {
        var settings = _settings.CurrentValue;
        if (!settings.IsConfigured)
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) " +
                "in configuration or user-secrets.");
        }

        return settings;
    }

    /// <summary>
    /// Runs a transport call and translates Maxio transport failures into the application's
    /// billing exceptions, so that callers never have to know about HTTP.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string description)
    {
        try
        {
            return await operation();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, description);
        }
    }

    private BillingException Translate(MaxioApiException ex, string description)
    {
        var message = $"Unable to {description} in Maxio.";

        return ex.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BillingConfigurationException(
                $"{message} Maxio rejected the credentials (HTTP {(int)ex.StatusCode}); check Maxio:ApiKey and Maxio:Subdomain."),
            _ when ex.IsValidationFailure => new BillingRequestInvalidException(message, ex.Errors, ex),
            _ => new BillingProviderException(message, ex.Errors, ex)
        };
    }
}
