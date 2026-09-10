using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maxio-backed implementation of <see cref="ISubscriptionBillingService"/>. Orchestrates the
/// low-level <see cref="IMaxioApiClient"/> to provide idempotent subscribe semantics and maps
/// Maxio wire models to the domain models the API layer consumes.
/// </summary>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Namespaced so that eShopOnWeb-owned customers never collide with unrelated Maxio
    // customers on the same site, and so the mapping is stable across app restarts (the
    // in-memory database does not persist it, but Maxio does, keyed by this reference).
    private const string ReferencePrefix = "eshoponweb:";

    // The offered plans require no payment method. Subscribe via remittance (invoiced) so no
    // immediate automatic charge is attempted at signup — which would otherwise fail with
    // "no payment method on file". Payment capture is out of scope for this subscribe flow.
    private const string PaymentCollectionMethod = "remittance";

    // Serializes subscribe operations per user within this process so a double-click cannot
    // race into two customers/subscriptions. Cross-process safety additionally relies on
    // Maxio's unique-reference guard and the pre-create active-subscription check below.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await Guarded(() => _client.ListProductsAsync(cancellationToken)).ConfigureAwait(false);

        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Where(p => p.ArchivedAt is null)
            .Where(p => string.Equals(p.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new PlanNotFoundException(planHandle ?? string.Empty);
        }

        // Confirm the requested plan is actually offered before touching customer/subscription
        // state, so an unknown handle is a clean 404 rather than a Maxio error.
        var plans = await GetAvailablePlansAsync(cancellationToken).ConfigureAwait(false);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new PlanNotFoundException(planHandle);
        }

        var reference = ToReference(subscriber.UserName);
        var gate = SubscribeLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, reference, cancellationToken).ConfigureAwait(false);

            // Idempotency: if the user already has a live subscription to this plan, return it
            // instead of creating a duplicate.
            var existing = await Guarded(() => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)).ConfigureAwait(false);
            var duplicate = existing
                .Select(MapSubscription)
                .FirstOrDefault(s => s.IsActive && string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
            {
                _logger.LogInformation($"User already subscribed to plan '{plan.Handle}' (subscription {duplicate.Id}); returning existing.");
                return new SubscribeResult { Subscription = duplicate, Created = false };
            }

            var created = await Guarded(() => _client.CreateSubscriptionAsync(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscriptionAttributes
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        PaymentCollectionMethod = PaymentCollectionMethod
                    }
                },
                cancellationToken)).ConfigureAwait(false);

            _logger.LogInformation($"Created subscription {created.Id} for customer {customer.Id} on plan '{plan.Handle}'.");
            return new SubscribeResult { Subscription = MapSubscription(created), Created = true };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var reference = ToReference(subscriber.UserName);
        var customer = await Guarded(() => _client.FindCustomerByReferenceAsync(reference, cancellationToken)).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await Guarded(() => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)).ConfigureAwait(false);
        return subscriptions
            .Select(MapSubscription)
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, string reference, CancellationToken cancellationToken)
    {
        var existing = await Guarded(() => _client.FindCustomerByReferenceAsync(reference, cancellationToken)).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.CreateCustomerAsync(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomerAttributes
                    {
                        FirstName = subscriber.FirstName,
                        LastName = subscriber.LastName,
                        Email = subscriber.Email,
                        Reference = reference
                    }
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation($"Created Maxio customer {created.Id} for reference '{reference}'.");
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a create race (reference now taken) — re-resolve the existing customer.
            var resolved = await Guarded(() => _client.FindCustomerByReferenceAsync(reference, cancellationToken)).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }

            throw ToUpstream(ex);
        }
    }

    private SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = checked((int)product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription)
    {
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new CustomerSubscription
        {
            Id = subscription.Id,
            State = subscription.State ?? "unknown",
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            PriceInCents = checked((int)priceInCents),
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static string ToReference(string userName) => ReferencePrefix + userName;

    private async Task<T> Guarded<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning($"Maxio API error (HTTP {ex.StatusCode}): {ex.Message}");
            throw ToUpstream(ex);
        }
    }

    private static BillingUpstreamException ToUpstream(MaxioApiException ex) =>
        new($"The billing provider returned an error: {ex.Message}", ex.StatusCode, ex);
}
