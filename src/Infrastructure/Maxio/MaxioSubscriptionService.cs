using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscribe flow against Maxio Advanced Billing, layering idempotency guarantees
/// on top of the raw <see cref="IMaxioApiClient"/>:
/// <list type="bullet">
/// <item>A single Maxio customer is maintained per shopper, keyed by the shopper's stable reference.</item>
/// <item>Concurrent subscribe requests for the same shopper are serialized so a double-click cannot
/// create two customers or two subscriptions.</item>
/// <item>If the shopper already has a live subscription to the requested plan, that subscription is
/// returned instead of creating a duplicate.</item>
/// </list>
/// </summary>
internal sealed class MaxioSubscriptionService : ISubscriptionService
{
    private const string DefaultCurrency = "USD";

    // Per-shopper gate so the ensure-customer + subscribe sequence runs atomically within this
    // process. Combined with lookup-before-create, this makes the subscribe flow idempotent.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriberLocks = new(StringComparer.Ordinal);

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioApiClient client, IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(FamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionValidationException("A plan handle is required to subscribe.");
        }

        planHandle = planHandle.Trim();

        // Confirm the requested plan is a real, available plan in the configured family before we
        // create or touch any billing records. This also yields the plan name for the response.
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var gate = SubscriberLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Idempotency: if the shopper already has a live subscription to this plan, return it.
            var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existingForPlan = existing
                .Select(Map)
                .FirstOrDefault(s => s.IsLive &&
                                     string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));
            if (existingForPlan is not null)
            {
                _logger.LogInformation(
                    $"Shopper '{subscriber.Reference}' already has live subscription {existingForPlan.Id} to plan '{plan.Handle}'; returning it.");
                return existingForPlan;
            }

            var created = await _client.CreateSubscriptionAsync(
                new CreateSubscriptionBody { ProductHandle = plan.Handle, CustomerId = customer.Id },
                cancellationToken);

            _logger.LogInformation(
                $"Created subscription {created.Id} to plan '{plan.Handle}' for shopper '{subscriber.Reference}' (customer {customer.Id}).");

            return Map(created);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customer = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        _logger.LogInformation($"Creating Maxio customer for shopper '{subscriber.Reference}'.");
        return await _client.CreateCustomerAsync(new CreateCustomerBody
        {
            FirstName = subscriber.FirstName,
            LastName = subscriber.LastName,
            Email = subscriber.Email,
            Reference = subscriber.Reference
        }, cancellationToken);
    }

    private string FamilyHandle =>
        _settings.ProductFamilyHandle
        ?? throw new InvalidOperationException("Maxio configuration is missing 'Maxio:ProductFamilyHandle'.");

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? string.Empty,
        currencyCode: DefaultCurrency);

    private static CustomerSubscription Map(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        var price = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : product?.PriceInCents ?? 0;

        return new CustomerSubscription(
            id: subscription.Id,
            state: subscription.State ?? "unknown",
            planHandle: product?.Handle ?? string.Empty,
            planName: product?.Name ?? product?.Handle ?? string.Empty,
            pricePerPeriodInCents: price,
            interval: product?.Interval ?? 0,
            intervalUnit: product?.IntervalUnit ?? string.Empty,
            currencyCode: string.IsNullOrWhiteSpace(subscription.Currency) ? DefaultCurrency : subscription.Currency!,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextBillingAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            createdAt: subscription.CreatedAt);
    }
}
