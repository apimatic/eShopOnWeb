using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionService"/> backed by Maxio Advanced Billing. Maps the eShopOnWeb user
/// onto a Maxio customer (keyed on a stable reference), enrolls them in plans, and reports their
/// subscriptions. Maxio is the system of record; nothing is persisted locally.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    // Maxio subscription states that mean the subscription is no longer live. A plan the user only
    // holds in one of these states can be freshly subscribed again; anything else is treated as an
    // existing live subscription for idempotency purposes.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    // Serializes ensure-customer + subscribe per user within the process so a double-click cannot
    // create two customers or two subscriptions. (This host runs single-instance with an in-memory
    // store; cross-process safety additionally relies on Maxio's per-site uniqueness of the customer
    // reference — see EnsureCustomerAsync.)
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    // Bills the subscription by remittance (invoice) so it activates without capturing a card. The
    // demo plans do not require a payment method; keeping this consistent means the subscribe flow
    // never needs card capture or 3-DS.
    private const string RemittanceCollection = "remittance";

    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioApiClient client, MaxioSettings settings, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        int familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = await _client.ListProductsForFamilyAsync(familyId, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user identity is required to subscribe.", nameof(userName));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required to subscribe.", nameof(planHandle));
        }

        // Confirm the plan exists in our family before touching the customer — surfaces bad input as a
        // clean 400 rather than a confusing Maxio validation error.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        string reference = BuildReference(userName);
        SemaphoreSlim gate = UserLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(userName, reference, cancellationToken);

            // Idempotency: if the user already has a live subscription to this plan, return it.
            var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var live = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                !IsTerminal(s.State));

            if (live is not null)
            {
                _logger.LogInformation("User {Reference} already has live subscription {SubscriptionId} to plan {Plan}; returning existing.",
                    reference, live.Id, plan.Handle);
                return new SubscribeResult(MapSubscription(live), alreadySubscribed: true);
            }

            var created = await _client.CreateSubscriptionAsync(new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = RemittanceCollection
            }, cancellationToken);

            _logger.LogInformation("Created subscription {SubscriptionId} for user {Reference} to plan {Plan} (state {State}).",
                created.Id, reference, plan.Handle, created.State);

            return new SubscribeResult(MapSubscription(created), alreadySubscribed: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user identity is required.", nameof(userName));
        }

        string reference = BuildReference(userName);
        var customer = await _client.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            // The user has never subscribed, so no Maxio customer exists yet.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    /// <summary>
    /// Finds an existing Maxio customer for the user by reference, creating one if none exists. The
    /// enclosing per-user lock prevents duplicate creation within the process; a concurrent create from
    /// another process would hit Maxio's per-site reference uniqueness (422), which we recover from by
    /// re-looking up the now-existing customer.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(string userName, string reference, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        (string firstName, string lastName, string email) = DeriveCustomerIdentity(userName);

        try
        {
            var created = await _client.CreateCustomerAsync(new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Likely a duplicate-reference race: another actor created the customer between our lookup
            // and our create. Re-look it up.
            var recovered = await _client.LookupCustomerByReferenceAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        string handle = _settings.ProductFamilyHandle!;
        var families = await _client.ListProductFamiliesAsync(cancellationToken);
        var family = families.FirstOrDefault(f => string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new MaxioConfigurationException(
                $"No Maxio product family with handle '{handle}' was found on this site. " +
                "Check the Maxio:ProductFamilyHandle setting.");
        }

        return family.Id;
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) via user-secrets or environment configuration.");
        }
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio:ProductFamilyHandle is not configured.");
        }
    }

    private static bool IsTerminal(string? state) => state is not null && TerminalStates.Contains(state);

    /// <summary>Stable, per-site-unique external key mapping the eShopOnWeb user to a Maxio customer.</summary>
    private static string BuildReference(string userName) => userName.Trim().ToLowerInvariant();

    private static (string FirstName, string LastName, string Email) DeriveCustomerIdentity(string userName)
    {
        string trimmed = userName.Trim();
        if (trimmed.Contains('@'))
        {
            string local = trimmed[..trimmed.IndexOf('@')];
            string first = string.IsNullOrWhiteSpace(local) ? "eShopOnWeb" : local;
            return (first, "eShopOnWeb", trimmed);
        }

        return (trimmed, "eShopOnWeb", $"{trimmed}@users.noreply.eshoponweb.local");
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        currency: "USD",
        intervalUnit: product.IntervalUnit ?? "month",
        intervalCount: product.Interval == 0 ? 1 : product.Interval,
        requiresPaymentMethod: product.RequireCreditCard,
        productId: product.Id);

    private static CustomerSubscription MapSubscription(MaxioSubscription s)
    {
        int priceInCents = s.ProductPriceInCents != 0 ? s.ProductPriceInCents : (s.Product?.PriceInCents ?? 0);

        return new CustomerSubscription(
            id: s.Id,
            state: s.State ?? "unknown",
            planHandle: s.Product?.Handle ?? string.Empty,
            planName: s.Product?.Name ?? s.Product?.Handle ?? string.Empty,
            priceInCents: priceInCents,
            currency: s.Currency ?? "USD",
            intervalUnit: s.Product?.IntervalUnit ?? "month",
            intervalCount: (s.Product?.Interval ?? 0) == 0 ? 1 : s.Product!.Interval,
            currentPeriodStartedAt: s.CurrentPeriodStartedAt,
            currentPeriodEndsAt: s.CurrentPeriodEndsAt,
            nextBillingDate: s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
            activatedAt: s.ActivatedAt,
            createdAt: s.CreatedAt,
            paymentCollectionMethod: s.PaymentCollectionMethod,
            customerId: s.Customer?.Id ?? 0,
            customerReference: s.Customer?.Reference);
    }
}
