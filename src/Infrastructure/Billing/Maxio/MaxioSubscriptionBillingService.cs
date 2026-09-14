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
/// Implements subscription billing against Maxio Advanced Billing.
/// <para>
/// Maxio is the system of record: nothing about plans, customers or subscriptions is stored
/// locally, and every answer is read back from Maxio. The link between an eShopOnWeb user and
/// their Maxio customer is the customer <c>reference</c>, derived deterministically from the user
/// name, which is why this survives a restart with no database of its own.
/// </para>
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PlanCacheKey = "maxio:plans";
    private const string SiteCacheKey = "maxio:site";

    /// <summary>
    /// Upper bound on how far a re-subscribe walks the chain of previous, ended subscriptions
    /// before giving up. A shopper cycling a plan more times than this in one call is not a real
    /// scenario; the bound is only here so a surprising Maxio response cannot spin.
    /// </summary>
    private const int MaxResubscribeAttempts = 8;

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _customerLocks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptionsMonitor<MaxioSettings> settings,
        IMemoryCache cache,
        KeyedAsyncLock customerLocks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _customerLocks = customerLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var settings = RequireConfiguredSettings();
        var familyHandle = settings.ProductFamilyHandle!;

        if (_cache.TryGetValue(PlanCacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var products = await _client.ListProductsForFamilyAsync(familyHandle, cancellationToken);

        var plans = products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => ToPlan(product, familyHandle))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.PlanCacheSeconds > 0)
        {
            _cache.Set<IReadOnlyList<SubscriptionPlan>>(
                PlanCacheKey, plans, TimeSpan.FromSeconds(settings.PlanCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscribeCommand command,
        CancellationToken cancellationToken = default)
    {
        var settings = RequireConfiguredSettings();

        // Resolving the plan through the configured family — rather than trusting the handle the
        // caller sent — is what stops a shopper subscribing to a product outside this catalog.
        var plan = (await ListPlansAsync(cancellationToken))
            .FirstOrDefault(candidate => string.Equals(candidate.Handle, command.PlanHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(command.PlanHandle, settings.ProductFamilyHandle!);

        var customerReference = MaxioReference.ForCustomer(command.Identity.UserName);

        using (await _customerLocks.AcquireAsync(customerReference, cancellationToken))
        {
            var customer = await EnsureCustomerAsync(command.Identity, customerReference, cancellationToken);

            var live = (await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(subscription =>
                    string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                    SubscriptionStates.IsLive(subscription.State));

            if (live is not null)
            {
                _logger.LogInformation(
                    "Customer {CustomerReference} is already subscribed to {PlanHandle} (subscription {SubscriptionId}); returning it.",
                    customerReference, plan.Handle, live.Id);

                return new SubscribeResult(ToSubscription(live, customerReference), SubscribeOutcome.AlreadySubscribed);
            }

            return await CreateSubscriptionAsync(command, plan, customer, customerReference, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        BillingIdentity identity,
        CancellationToken cancellationToken = default)
    {
        RequireConfiguredSettings();

        if (identity is null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        var customerReference = MaxioReference.ForCustomer(identity.UserName);
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);

        if (customer is null)
        {
            // No billing customer yet simply means the shopper has never subscribed.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(subscription => ToSubscription(subscription, customerReference))
            .OrderByDescending(subscription => subscription.IsLive)
            .ThenByDescending(subscription => subscription.ActivatedAt ?? subscription.CurrentPeriodStartsAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(subscription => subscription.Id)
            .ToList();
    }

    /// <summary>
    /// Returns the shopper's Maxio customer, creating it on first use. Safe to call concurrently:
    /// Maxio rejects a second customer with the same reference, and that rejection is resolved by
    /// reading back the one that won.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(
        BillingIdentity identity,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(identity);

        try
        {
            var created = await _client.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    Reference = customerReference,
                    Email = identity.Email,
                    FirstName = firstName,
                    LastName = lastName
                },
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id, customerReference);

            return created;
        }
        catch (BillingValidationException exception) when (IsDuplicateReference(exception))
        {
            // Another instance created the customer between our lookup and our create.
            var raced = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);

            if (raced is null)
            {
                throw;
            }

            _logger.LogInformation(
                "Maxio customer {CustomerId} for reference {CustomerReference} was created concurrently; using it.",
                raced.Id, customerReference);

            return raced;
        }
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        SubscribeCommand command,
        SubscriptionPlan plan,
        MaxioCustomer customer,
        string customerReference,
        CancellationToken cancellationToken)
    {
        // With no caller-supplied key, the plan handle is the key: it makes a repeated subscribe
        // to the same plan resolve to the same reference, which is what defuses a double-click.
        var idempotencyKey = command.IdempotencyKey ?? plan.Handle;
        var baseReference = MaxioReference.ForSubscription(customerReference, idempotencyKey);
        var collectionMethod = await ResolvePaymentCollectionMethodAsync(cancellationToken);
        var reference = baseReference;

        for (var attempt = 1; attempt <= MaxResubscribeAttempts; attempt++)
        {
            try
            {
                var created = await _client.CreateSubscriptionAsync(
                    new MaxioCreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        Reference = reference,
                        PaymentCollectionMethod = collectionMethod
                    },
                    cancellationToken);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} ({Reference}) on plan {PlanHandle} for customer {CustomerId}, state {State}.",
                    created.Id, reference, plan.Handle, customer.Id, created.State);

                return new SubscribeResult(ToSubscription(created, customerReference), SubscribeOutcome.Created);
            }
            catch (BillingValidationException exception) when (IsDuplicateReference(exception))
            {
                var owner = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);

                if (owner is null)
                {
                    // The reference is taken but not readable — nothing safe to return.
                    throw;
                }

                if (SubscriptionStates.IsLive(owner.State) || command.IdempotencyKey is not null)
                {
                    // Either the earlier request already produced a usable subscription, or the
                    // caller pinned an explicit key and is entitled to the same answer as before.
                    _logger.LogInformation(
                        "Subscription {Reference} already exists as {SubscriptionId} ({State}); replaying it.",
                        reference, owner.Id, owner.State);

                    return new SubscribeResult(ToSubscription(owner, customerReference), SubscribeOutcome.IdempotentReplay);
                }

                // The derived reference belongs to a subscription that has ended, so this is a
                // deliberate re-subscribe. Move to a reference derived from the one it replaces,
                // which keeps a repeated re-subscribe collapsing onto a single new subscription.
                reference = MaxioReference.ForResubscribe(baseReference, owner.Id);

                _logger.LogInformation(
                    "Previous subscription {SubscriptionId} to {PlanHandle} is {State}; re-subscribing as {Reference}.",
                    owner.Id, plan.Handle, owner.State, reference);
            }
        }

        throw new BillingGatewayException(
            $"Could not find a free subscription reference for plan '{plan.Handle}' after {MaxResubscribeAttempts} attempts.");
    }

    /// <summary>
    /// The collection method to enroll with. Explicit configuration wins; otherwise the site is
    /// asked once, because the method that permits a signup with no payment method on file is
    /// "remittance" under Relationship Invoicing and "invoice" on legacy sites.
    /// </summary>
    private async Task<string> ResolvePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var configured = _settings.CurrentValue.PaymentCollectionMethod;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!.Trim();
        }

        var site = await GetSiteAsync(cancellationToken);

        return site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
    }

    private async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCacheKey, out MaxioSite? cached) && cached is not null)
        {
            return cached;
        }

        var site = await _client.GetSiteAsync(cancellationToken);
        _cache.Set(SiteCacheKey, site, TimeSpan.FromMinutes(30));

        return site;
    }

    private MaxioSettings RequireConfiguredSettings()
    {
        var settings = _settings.CurrentValue;
        var problems = settings.Validate();

        if (problems.Count > 0)
        {
            throw new BillingConfigurationException(problems);
        }

        return settings;
    }

    /// <summary>
    /// Recognises Maxio's "that reference is already taken" rejection. Maxio expresses it only in
    /// the human-readable message ("Reference: must be unique - that value has been taken."), so
    /// the match is kept deliberately loose.
    /// </summary>
    private static bool IsDuplicateReference(BillingValidationException exception) =>
        exception.Errors.Any(error =>
            error.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
            (error.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
             error.Contains("taken", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Maxio requires a first and last name on every customer, but eShopOnWeb identities carry
    /// only a user name, so a readable pair is derived from it when the caller supplies nothing.
    /// </summary>
    private static (string FirstName, string LastName) ResolveName(BillingIdentity identity)
    {
        var firstName = identity.FirstName?.Trim();
        var lastName = identity.LastName?.Trim();

        if (!string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(lastName))
        {
            return (firstName!, lastName!);
        }

        var localPart = identity.UserName.Split('@')[0];
        var words = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .ToList();

        if (words.Count == 0)
        {
            words.Add("eShopOnWeb");
        }

        return (
            string.IsNullOrEmpty(firstName) ? words[0] : firstName!,
            string.IsNullOrEmpty(lastName)
                ? (words.Count > 1 ? string.Join(" ", words.Skip(1)) : "eShopOnWeb")
                : lastName!);
    }

    private static string Capitalize(string word) =>
        word.Length <= 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word[1..];

    private static SubscriptionPlan ToPlan(MaxioProduct product, string familyHandle) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? familyHandle,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit
    };

    private static CustomerSubscription ToSubscription(MaxioSubscription subscription, string customerReference) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = string.IsNullOrWhiteSpace(subscription.Currency) ? "USD" : subscription.Currency!,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodStartsAt = ParseTimestamp(subscription.CurrentPeriodStartedAt),
        CurrentPeriodEndsAt = ParseTimestamp(subscription.CurrentPeriodEndsAt),
        NextBillingAt = ParseTimestamp(subscription.NextAssessmentAt),
        ActivatedAt = ParseTimestamp(subscription.ActivatedAt),
        CanceledAt = ParseTimestamp(subscription.CanceledAt),
        ExpiresAt = ParseTimestamp(subscription.ExpiresAt),
        TrialEndsAt = ParseTimestamp(subscription.TrialEndedAt),
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        BalanceInCents = subscription.BalanceInCents,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference ?? customerReference
    };

    /// <summary>
    /// Maxio timestamps carry the site's UTC offset (for example 2026-09-06T10:03:57+05:00), so
    /// they are read as <see cref="DateTimeOffset"/> and the offset is preserved rather than
    /// silently reinterpreted in the server's local time zone.
    /// </summary>
    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
