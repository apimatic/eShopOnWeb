using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing, the billing system of record.
/// No subscription state is mirrored locally; every operation reads through to Maxio.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// States in which a subscription no longer entitles the customer, so subscribing again may
    /// create a fresh subscription. Anything else (active, trialing, past_due, on_hold, ...) counts
    /// as an existing enrollment and is returned instead of creating a duplicate.
    /// </summary>
    private static readonly HashSet<string> ResubscribableStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromMinutes(5);

    private readonly IMaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    // Serializes subscribe attempts per customer within this process, so a double-click (or concurrent
    // retries) can never race past the existing-subscription check and create two subscriptions.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

    public MaxioSubscriptionBillingService(
        IMaxioClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = $"maxio-plans:{_settings.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached != null)
        {
            return cached;
        }

        var products = await _client.ListProductsForProductFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);

        var plans = products
            .Where(p => p.ArchivedAt == null)
            .OrderBy(p => p.PriceInCents)
            .Select(p => new SubscriptionPlan
            {
                ProductId = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty
            })
            .ToList();

        _cache.Set(cacheKey, plans, PlanCacheDuration);
        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var plan = (await ListSubscriptionPlansAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            throw new SubscriptionPlanNotFoundException(request.PlanHandle);
        }

        var customerLock = _subscribeLocks.GetOrAdd(request.CustomerReference, _ => new SemaphoreSlim(1, 1));
        await customerLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                !ResubscribableStates.Contains(s.State ?? string.Empty));

            if (existing != null)
            {
                _logger.LogInformation(
                    "Customer {CustomerReference} already holds subscription {SubscriptionId} for plan {PlanHandle}; returning it instead of creating a duplicate.",
                    request.CustomerReference, existing.Id, plan.Handle);
                return new SubscribeResult(Map(existing), alreadyExisted: true);
            }

            var created = await _client.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = "remittance",
                Reference = $"{request.CustomerReference}:{plan.Handle}"
            }, cancellationToken);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} for customer {CustomerReference} on plan {PlanHandle}.",
                created.Id, request.CustomerReference, plan.Handle);

            return new SubscribeResult(Map(created), alreadyExisted: false);
        }
        finally
        {
            customerLock.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    /// <summary>
    /// Returns the billing customer for the reference, creating it on first use. The reference carries a
    /// server-side uniqueness constraint, so a concurrent create loses the race with a 422 and falls back
    /// to looking up the winner — a customer is never created twice for the same reference.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(new MaxioCustomerAttributes
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.CustomerReference
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (HttpStatusCode)422)
        {
            var racedWinner = await _client.FindCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
            if (racedWinner != null)
            {
                return racedWinner;
            }

            throw;
        }
    }

    private static CustomerSubscription Map(MaxioSubscription subscription)
    {
        return new CustomerSubscription
        {
            SubscriptionId = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
            // next_assessment_at is when payment capture is next attempted; it normally tracks the period end.
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };
    }
}
