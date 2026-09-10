using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing.
/// </summary>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Subscriptions in these states are terminal/dead: a customer in one of them is
    // not considered "currently subscribed", so re-subscribing creates a fresh one.
    // Any other state (active, trialing, assessing, past_due, soft_failure, ...) counts
    // as a live enrollment and short-circuits to avoid duplicates on double-submit.
    private static readonly HashSet<string> TerminalStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "cancelled", "expired", "unpaid", "trial_ended", "failed_to_create" };

    // Invoice-based collection. The subscribe flow deliberately captures no card / 3-DS,
    // so balances are billed by remittance; this lets subscribe succeed for the seeded
    // "payment method not required" plans without a payment profile on file.
    private const string RemittanceCollection = "remittance";

    private readonly IMaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = await _client.ListProductsForFamilyAsync(familyId, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new PlanNotFoundException(planHandle ?? string.Empty);
        }

        // Validate the plan against the configured catalog before touching the customer,
        // so an unknown plan is a clean 404 with no side effects.
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new PlanNotFoundException(planHandle);
        }

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        // Idempotency: if the customer already has a live subscription to this plan,
        // return it instead of creating a duplicate (protects against double-clicks).
        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerId} already has a live subscription {SubscriptionId} to plan {PlanHandle}; returning it.",
                customer.Id, existing.Id, plan.Handle);
            return new SubscribeResult(MapSubscription(existing), AlreadyExisted: true);
        }

        var created = await _client.CreateSubscriptionAsync(
            new CreateSubscriptionBody
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = RemittanceCollection,
            },
            cancellationToken);

        _logger.LogInformation(
            "Created subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {PlanHandle}.",
            created.Id, created.State, customer.Id, plan.Handle);

        return new SubscribeResult(MapSubscription(created), AlreadyExisted: false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        var customer = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            // No customer record yet means the user has never subscribed.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(MapSubscription)
            .ToList();
    }

    /// <summary>Ensures a Maxio customer keyed by the subscriber's reference. Idempotent and race-safe.</summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.CreateCustomerAsync(
                new CreateCustomerBody
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference,
                },
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, subscriber.Reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request likely created the customer first (reference must be
            // unique). Re-lookup and use the winner rather than failing the caller.
            var afterRace = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (afterRace is not null)
            {
                _logger.LogInformation("Reused Maxio customer {CustomerId} after a concurrent create for reference {Reference}.", afterRace.Id, subscriber.Reference);
                return afterRace;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !IsTerminal(s.State));
    }

    private static bool IsTerminal(string? state) =>
        !string.IsNullOrEmpty(state) && TerminalStates.Contains(state);

    private async Task<long> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var families = await _client.ListProductFamiliesAsync(cancellationToken);
        var family = families.FirstOrDefault(f =>
            string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new SubscriptionBillingException(
                $"Configured Maxio product family '{_settings.ProductFamilyHandle}' was not found on site '{_settings.Subdomain}'.");
        }

        return family.Id;
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard,
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
    };
}
