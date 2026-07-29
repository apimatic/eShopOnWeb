using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio-backed implementation of <see cref="ISubscriptionBillingService"/>. Orchestrates the
/// hero "Subscribe" flow: ensure a single Maxio customer exists for the eShopOnWeb user
/// (keyed by a stable reference), then enroll them idempotently so a double-click never
/// creates two customers or two subscriptions.
/// </summary>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Maxio subscription states that mean "no longer enrolled"; anything else counts as a live
    // enrollment that the idempotency guard should return instead of creating a duplicate.
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended",
    };

    // Per-user in-process gate: serializes concurrent subscribe calls for the same reference so
    // a burst (e.g. a double-click) is handled one-at-a-time and the second sees the first's result.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private readonly MaxioApiClient _client;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly string _paymentCollectionMethod;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionBillingService(
        MaxioApiClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _logger = logger;
        _paymentCollectionMethod = string.IsNullOrWhiteSpace(settings.Value.PaymentCollectionMethod)
            ? "remittance"
            : settings.Value.PaymentCollectionMethod.Trim();
        _productFamilyHandle = settings.Value.ProductFamilyHandle ?? string.Empty;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListFamilyProductsAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    private SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        ProductFamilyHandle = _productFamilyHandle,
    };

    public async Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        planHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle)).Trim();

        // Validate the plan against the configured product family (also gives us its details).
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                   ?? throw new PlanNotFoundException(planHandle);

        var gate = SubscribeGates.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Idempotency: if the user already has a live subscription to this plan, return it.
            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Reusing existing live subscription {SubscriptionId} to plan {Plan} for customer {CustomerId}.",
                    existing.Id, plan.Handle, customer.Id);
                return MapSubscription(existing, customer.Id, alreadyExisted: true);
            }

            try
            {
                var created = await _client.CreateSubscriptionAsync(
                    new CreateSubscriptionBody
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        PaymentCollectionMethod = _paymentCollectionMethod,
                    },
                    NewUniquenessToken(),
                    cancellationToken);

                _logger.LogInformation(
                    "Created subscription {SubscriptionId} to plan {Plan} for customer {CustomerId} ({State}).",
                    created.Id, plan.Handle, customer.Id, created.State);
                return MapSubscription(created, customer.Id, alreadyExisted: false);
            }
            catch (MaxioDuplicateSubmissionException)
            {
                // A concurrent/duplicate create won the race; reconcile to the winning subscription.
                var reconciled = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
                if (reconciled is not null)
                {
                    return MapSubscription(reconciled, customer.Id, alreadyExisted: true);
                }

                throw new BillingUpstreamException(
                    "Maxio reported a duplicate subscription, but no matching live subscription could be found to return.");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        var customer = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            // No Maxio customer yet for this user — read-only path never creates one.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(s => MapSubscription(s, customer.Id, alreadyExisted: false))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerBody
        {
            FirstName = subscriber.FirstName,
            LastName = subscriber.LastName,
            Email = subscriber.Email,
            Reference = subscriber.Reference,
        };

        try
        {
            var created = await _client.CreateCustomerAsync(body, NewUniquenessToken(), cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, subscriber.Reference);
            return created;
        }
        catch (Exception ex) when (ex is MaxioDuplicateSubmissionException or BillingUpstreamException)
        {
            // Lost a create race (duplicate submission, or reference-already-taken 422): the
            // customer now exists — re-read by reference and use it.
            var reconciled = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && IsLiveState(s.State));
    }

    private static bool IsLiveState(string? state) =>
        !string.IsNullOrEmpty(state) && !EndOfLifeStates.Contains(state);

    private static string NewUniquenessToken() => Guid.NewGuid().ToString("N");

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, long customerId, bool alreadyExisted)
    {
        var product = subscription.Product;
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : product?.PriceInCents ?? 0;

        return new CustomerSubscription
        {
            Id = subscription.Id,
            CustomerId = customerId,
            PlanHandle = product?.Handle ?? "(no plan)",
            PlanName = product?.Name ?? "(no plan)",
            State = subscription.State ?? "unknown",
            PriceInCents = priceInCents,
            Interval = product?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit ?? string.Empty,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? string.Empty,
            AlreadyExisted = alreadyExisted,
        };
    }
}
