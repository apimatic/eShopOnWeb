using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio-backed implementation of <see cref="ISubscriptionManagementService"/>. Orchestrates the
/// confirmed Maxio calls and adds the behaviour the API needs: idempotent customer/subscription
/// creation, provider-to-domain mapping, and short-lived plan caching.
/// </summary>
internal sealed class MaxioSubscriptionService : ISubscriptionManagementService
{
    // Subscriptions in these states are finished and do not block re-subscribing to the same plan.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "trial_ended", "failed_to_create"
    };

    // Card-free enrollment: bill by remittance (invoice) so no stored payment method is required.
    private const string RemittanceCollection = "remittance";

    // Per-user in-process serialization so a double-click cannot create two customers/subscriptions.
    // Keyed by subscriber reference. Static because the service is registered per-request (scoped).
    // NOTE: guards a single process instance; horizontal scaling would need provider-side dedup.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioApiClient _api;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(
        IMaxioApiClient api,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _api = api;
        _cache = cache;
        _logger = logger;

        var family = settings.Value.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(family))
        {
            throw new SubscriptionConfigurationException("Maxio:ProductFamilyHandle must be configured.");
        }

        _productFamilyHandle = family.Trim();
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = $"maxio:plans:{_productFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var products = await _api.ListProductsForFamilyAsync(_productFamilyHandle, cancellationToken).ConfigureAwait(false);
        var plans = products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .OrderBy(p => p.Price)
            .ToList();

        _cache.Set(cacheKey, (IReadOnlyList<SubscriptionPlan>)plans, TimeSpan.FromSeconds(60));
        return plans;
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(
        SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionPlanNotFoundException(planHandle ?? "(none)");
        }

        var plans = await ListPlansAsync(cancellationToken).ConfigureAwait(false);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var gate = SubscribeLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken).ConfigureAwait(false);

            // Idempotency: if the user already holds a live subscription to this plan, return it.
            var existing = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);
            var live = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)
                && !TerminalStates.Contains(s.State ?? string.Empty));

            if (live is not null)
            {
                _logger.LogInformation(
                    "Subscriber {Reference} already has live subscription {SubscriptionId} to plan {Plan}; returning existing.",
                    subscriber.Reference, live.Id, plan.Handle);
                return new SubscriptionEnrollmentResult { Subscription = ToSummary(live, plan), AlreadyExisted = true };
            }

            var created = await _api.CreateSubscriptionAsync(
                new CreateSubscriptionWire
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = RemittanceCollection,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} ({State}) for subscriber {Reference} on plan {Plan}.",
                created.Id, created.State, subscriber.Reference, plan.Handle);

            return new SubscriptionEnrollmentResult { Subscription = ToSummary(created, plan), AlreadyExisted = false };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        var customer = await _api.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);
        return subscriptions.Select(s => ToSummary(s, planContext: null)).ToList();
    }

    private async Task<CustomerWire> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _api.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(subscriber);
        try
        {
            return await _api.CreateCustomerAsync(
                new CreateCustomerWire
                {
                    Email = subscriber.Email,
                    Reference = subscriber.Reference,
                    FirstName = firstName,
                    LastName = lastName,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (SubscriptionBillingException)
        {
            // A concurrent create (or a duplicate reference) may have won the race. Re-look up before failing.
            var afterRace = await _api.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken).ConfigureAwait(false);
            if (afterRace is not null)
            {
                return afterRace;
            }

            throw;
        }
    }

    /// <summary>Maxio requires non-blank first and last names; derive sensible values from the email when absent.</summary>
    private static (string FirstName, string LastName) DeriveName(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();

        if (string.IsNullOrEmpty(first))
        {
            var atIndex = subscriber.Email.IndexOf('@');
            var local = atIndex > 0 ? subscriber.Email[..atIndex] : subscriber.Email;
            first = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        }

        if (string.IsNullOrEmpty(last))
        {
            last = "Subscriber";
        }

        return (first, last);
    }

    private SubscriptionPlan ToPlan(ProductWire product) => new()
    {
        Handle = product.Handle!,
        Name = string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        Description = product.Description,
        Price = product.PriceInCents / 100m,
        IntervalCount = product.Interval,
        IntervalUnit = string.IsNullOrWhiteSpace(product.IntervalUnit) ? "month" : product.IntervalUnit!,
        RequiresPaymentMethod = product.RequireCreditCard,
    };

    private static SubscriptionSummary ToSummary(SubscriptionWire subscription, SubscriptionPlan? planContext)
    {
        var product = subscription.Product;

        var priceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents;
        var price = priceInCents.HasValue ? priceInCents.Value / 100m : planContext?.Price ?? 0m;

        var intervalCount = product is { Interval: > 0 } ? product.Interval : planContext?.IntervalCount ?? 0;

        return new SubscriptionSummary
        {
            Id = subscription.Id,
            State = subscription.State ?? "unknown",
            PlanHandle = product?.Handle ?? planContext?.Handle ?? string.Empty,
            PlanName = product?.Name ?? planContext?.Name ?? product?.Handle ?? planContext?.Handle ?? string.Empty,
            Price = price,
            IntervalUnit = product?.IntervalUnit ?? planContext?.IntervalUnit ?? "month",
            IntervalCount = intervalCount,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? RemittanceCollection,
        };
    }
}
