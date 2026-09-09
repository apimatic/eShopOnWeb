using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio-backed implementation of <see cref="ISubscriptionBillingService"/>. Owns the mapping from
/// eShopOnWeb user identity to a Maxio customer (via a stable reference) and the idempotency logic
/// that makes subscribe safe to call more than once.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // Prefix keeps eShopOnWeb-owned customers distinct from anything else on a shared Maxio site.
    private const string ReferencePrefix = "eshoponweb:";

    // Serializes the ensure-customer + subscribe flow per user so a rapid double-submit within this
    // process cannot create two customers/subscriptions. Combined with the reference-based lookups
    // and Maxio's unique-reference enforcement, this makes the hero flow idempotent.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new(StringComparer.Ordinal);

    // States that still represent a live enrollment; a duplicate subscribe returns the existing one.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create",
    };

    private readonly MaxioApiClient _api;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioApiClient api, IOptions<MaxioSettings> options, ILogger<MaxioBillingService> logger)
    {
        _api = api;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        _settings.EnsureConfigured();

        var products = await _api.ListProductsForFamilyAsync(_settings.ProductFamilyHandle!, cancellationToken);

        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionBillingException("A user identity is required to subscribe.");
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionPlanNotFoundException(planHandle ?? string.Empty);
        }

        _settings.EnsureConfigured();

        // Validate the requested plan exists within the configured product family. This both
        // protects against subscribing to arbitrary handles and lets us enrich the result.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                   ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var reference = BuildReference(userName);
        var gate = UserGates.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(userName, reference, cancellationToken);

            // Idempotency: if the user already has a live subscription to this plan, return it
            // rather than creating a duplicate (handles double-clicks / retries).
            var existing = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var live = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                IsLive(s.State));

            if (live is not null)
            {
                _logger.LogInformation("Returning existing {State} subscription {Id} for reference {Reference} / plan {Plan}.",
                    live.State, live.Id, reference, plan.Handle);
                return MapSubscription(live, reference, plan);
            }

            var created = await _api.CreateSubscriptionAsync(
                new CreateSubscriptionBody
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    // The seeded plans do not require a payment method; remittance (invoice) collection
                    // lets the subscription activate without card capture / 3-DS.
                    PaymentCollectionMethod = "remittance",
                },
                cancellationToken);

            _logger.LogInformation("Created subscription {Id} ({State}) for reference {Reference} / plan {Plan}.",
                created.Id, created.State, reference, plan.Handle);

            return MapSubscription(created, reference, plan);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionBillingException("A user identity is required to list subscriptions.");
        }

        _settings.EnsureConfigured();

        var reference = BuildReference(userName);
        var customer = await _api.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => MapSubscription(s, reference, plan: null)).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userName, string reference, CancellationToken cancellationToken)
    {
        var existing = await _api.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _api.CreateCustomerAsync(BuildCustomer(userName, reference), cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 422 or 409)
        {
            // A concurrent request (or a prior partial run) already created the customer for this
            // unique reference. Re-read it instead of failing.
            _logger.LogInformation("Customer create conflicted for reference {Reference}; re-reading existing customer.", reference);
            var after = await _api.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (after is not null)
            {
                return after;
            }

            throw;
        }
    }

    private static bool IsLive(string? state)
        => !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);

    private static string BuildReference(string userName)
        => ReferencePrefix + userName.Trim().ToLowerInvariant();

    private static CreateCustomerBody BuildCustomer(string userName, string reference)
    {
        var email = userName.Contains('@', StringComparison.Ordinal)
            ? userName
            : $"{userName}@users.eshoponweb.local";

        var localPart = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;

        return new CreateCustomerBody
        {
            FirstName = firstName,
            LastName = "eShopOnWeb",
            Email = email,
            Reference = reference,
        };
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        FormattedPrice = FormatMoney(product.PriceInCents, "USD"),
        Currency = "USD",
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamily?.Handle,
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, string reference, SubscriptionPlan? plan)
    {
        var currency = string.IsNullOrWhiteSpace(subscription.Currency) ? "USD" : subscription.Currency!;
        var priceInCents = subscription.CurrentBillingAmountInCents
                           ?? subscription.ProductPriceInCents
                           ?? subscription.Product?.PriceInCents
                           ?? plan?.PriceInCents
                           ?? 0;

        return new CustomerSubscription
        {
            Id = subscription.Id,
            State = subscription.State ?? "unknown",
            PlanHandle = subscription.Product?.Handle ?? plan?.Handle,
            PlanName = subscription.Product?.Name ?? plan?.Name,
            PriceInCents = priceInCents,
            FormattedPrice = FormatMoney(priceInCents, currency),
            Currency = currency,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CustomerReference = subscription.Customer?.Reference ?? reference,
            CustomerId = subscription.Customer?.Id ?? 0,
        };
    }

    private static string FormatMoney(long amountInCents, string currency)
    {
        var amount = amountInCents / 100m;
        return string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
            ? "$" + amount.ToString("0.00", CultureInfo.InvariantCulture)
            : amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + currency;
    }
}
