using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates eShopOnWeb users against Maxio Advanced Billing:
///  - customers are keyed by a stable per-user reference, created on first use (idempotent;
///    Maxio enforces reference uniqueness server-side, which closes the create race)
///  - subscribing is protected against double-click: per-user serialization plus an
///    existing-active-subscription check before a new subscription is created
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states that represent a live subscription for our purposes.
    private static readonly HashSet<string> ActiveStates = new(StringComparer.OrdinalIgnoreCase)
        { "active", "trialing", "past_due", "unpaid", "assessment" };

    private readonly IMaxioClient _client;
    private readonly MaxioOptions _options;

    // Serializes subscribe operations per user reference within this process, so a
    // double-click does not interleave ensure-customer/create-subscription calls.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    public MaxioSubscriptionService(IMaxioClient client, MaxioOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireProductFamilyHandle();
        var products = await _client.ListProductsAsync(cancellationToken);
        return products
            .Where(p => string.Equals(p.ProductFamily?.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                MaxioProductId = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents ?? 0,
                Interval = p.Interval ?? 1,
                IntervalUnit = p.IntervalUnit ?? "month",
                ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userReference, string email, string? planHandle = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
        {
            throw new ArgumentException("A user reference is required to subscribe.", nameof(userReference));
        }

        var familyHandle = RequireProductFamilyHandle();
        var plans = await GetPlansAsync(cancellationToken);
        if (plans.Count == 0)
        {
            throw new MaxioApiException(System.Net.HttpStatusCode.NotFound,
                new[] { $"No subscription plans are configured in the '{familyHandle}' product family." });
        }

        var plan = planHandle is null
            ? plans[0]
            : plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
              ?? throw new MaxioApiException(System.Net.HttpStatusCode.NotFound,
                  new[] { $"Plan '{planHandle}' was not found in product family '{familyHandle}'." });

        var lockKey = userReference.Trim().ToLowerInvariant();
        var userLock = UserLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var (customer, createdCustomer) = await EnsureCustomerAsync(userReference, email, cancellationToken);

            // Double-click protection: reuse an existing live subscription on the same plan.
            var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existingForPlan = existing.FirstOrDefault(s =>
                ActiveStates.Contains(s.State)
                && string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
            if (existingForPlan is not null)
            {
                return new SubscribeResult
                {
                    Subscription = Map(existingForPlan),
                    AlreadySubscribed = true,
                    CreatedCustomer = createdCustomer
                };
            }

            var subscription = await _client.CreateSubscriptionAsync(customer.Id, plan.MaxioProductId, cancellationToken);
            return new SubscribeResult
            {
                Subscription = Map(subscription),
                AlreadySubscribed = false,
                CreatedCustomer = createdCustomer
            };
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(s => ActiveStates.Contains(s.State))
            .Select(Map)
            .ToList();
    }

    private async Task<(MaxioCustomer Customer, bool Created)> EnsureCustomerAsync(string userReference, string email, CancellationToken cancellationToken)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is not null)
        {
            return (customer, false);
        }

        var request = new MaxioCreateCustomerRequest
        {
            Reference = userReference,
            Email = email,
            FirstName = "eShopOnWeb",
            LastName = userReference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken);
            return (created, true);
        }
        catch (MaxioApiException ex) when (ex.IsValidationConflict)
        {
            // Lost a create race (Maxio enforces unique references). The winning
            // request made the customer visible; re-fetch instead of failing.
            var raced = await _client.FindCustomerByReferenceAsync(userReference, cancellationToken);
            if (raced is not null)
            {
                return (raced, false);
            }
            throw;
        }
    }

    // Plan handles are stable; numeric product IDs are not guaranteed across
    // reseeds, so the handle is the source of truth and the id is resolved from
    // the live catalog on every call (see GetPlansAsync).

    private string RequireProductFamilyHandle()
    {
        var handle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: Maxio:ProductFamilyHandle is required (from MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
        return handle;
    }

    private static SubscriptionSummary Map(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        return new SubscriptionSummary
        {
            MaxioSubscriptionId = subscription.Id,
            State = subscription.State,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PriceInCents = product?.PriceInCents ?? 0,
            Interval = product?.Interval ?? 1,
            IntervalUnit = product?.IntervalUnit ?? "month",
            // Verified live: invoice-mode subscriptions carry the period end here.
            NextBillingDate = subscription.NextBillingAt ?? subscription.CurrentPeriodEndsAt,
            CustomerReference = subscription.Customer?.Reference ?? string.Empty
        };
    }
}
