using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing. Enforces the
/// idempotency guarantees the subscribe flow requires:
/// <list type="bullet">
///   <item>the billing customer is resolved by a deterministic <c>reference</c> (lookup-then-create);</item>
///   <item>an in-process per-user lock serialises concurrent subscribe calls (double-click);</item>
///   <item>an existing live subscription to the same plan is returned instead of creating a duplicate;</item>
///   <item>a <c>uniqueness_token</c> guards the create calls against duplicate submissions across instances.</item>
/// </list>
/// </summary>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Chargify subscription states that mean the enrollment is no longer usable. Anything else
    // (active, trialing, assessing, pending, past_due, soft_failure, on_hold, …) counts as live.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "cancelled", "expired", "unpaid", "trial_ended", "failed_to_create"
    };

    // Shared across the scoped instances so double-submits within one process serialise per user.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ReferenceLocks = new();

    private static readonly TimeSpan FamilyCacheDuration = TimeSpan.FromMinutes(10);

    private readonly MaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioApiClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();
        var products = await GetFamilyProductsAsync(cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _options.Validate();

        // Validate the requested plan against the offered catalog before touching the customer.
        var products = await GetFamilyProductsAsync(cancellationToken);
        var product = products.FirstOrDefault(p =>
            p.ArchivedAt is null && string.Equals(p.Handle, command.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(command.PlanHandle);
        }

        var reference = command.Customer.Reference;
        var gate = ReferenceLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(command.Customer, cancellationToken);

            // Idempotency: return an existing live subscription to this plan rather than duplicating.
            var existing = await FindLiveSubscriptionAsync(customer.Id, product.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Customer {CustomerId} already has live subscription {SubscriptionId} to plan {PlanHandle}; returning it.",
                    customer.Id, existing.Id, product.Handle);
                return MapSubscription(existing);
            }

            var attributes = new SubscriptionAttributes
            {
                CustomerId = customer.Id,
                ProductHandle = product.Handle,
                PaymentCollectionMethod = "remittance"
            };

            MaxioSubscription created;
            try
            {
                created = await _client.CreateSubscriptionAsync(
                    attributes, UniquenessToken("subscribe", reference, product.Handle), cancellationToken);
            }
            catch (MaxioDuplicateSubmissionException)
            {
                // A concurrent/previous submission was accepted; reconcile by re-reading.
                var reconciled = await FindLiveSubscriptionAsync(customer.Id, product.Handle, cancellationToken);
                if (reconciled is null)
                {
                    throw new BillingServiceException(
                        "A duplicate subscription request was detected but the resulting subscription could not be located. Please retry shortly.");
                }

                return MapSubscription(reconciled);
            }

            _logger.LogInformation(
                "Created subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {PlanHandle}.",
                created.Id, created.State, customer.Id, product.Handle);

            return MapSubscription(created);
        }
        catch (MaxioApiException ex) when (ex is not MaxioDuplicateSubmissionException)
        {
            throw new BillingServiceException($"Maxio could not complete the subscription request: {ex.Message}", ex);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(BillingCustomerIdentity customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        _options.Validate();

        try
        {
            var existing = await _client.LookupCustomerByReferenceAsync(customer.Reference, cancellationToken);
            if (existing is null)
            {
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.GetCustomerSubscriptionsAsync(existing.Id, cancellationToken);
            return subscriptions
                .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
                .Select(MapSubscription)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw new BillingServiceException($"Maxio could not return the customer's subscriptions: {ex.Message}", ex);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingCustomerIdentity identity, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(identity.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var attributes = new CustomerAttributes
        {
            FirstName = identity.FirstName,
            LastName = identity.LastName,
            Email = identity.Email,
            Reference = identity.Reference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(
                attributes, UniquenessToken("customer", identity.Reference), cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, identity.Reference);
            return created;
        }
        catch (MaxioApiException) // 409 duplicate submission or 422 reference-taken from a race: reconcile by lookup.
        {
            var reconciled = await _client.LookupCustomerByReferenceAsync(identity.Reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.GetCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && !TerminalStates.Contains(s.State));
    }

    private async Task<IReadOnlyList<MaxioProduct>> GetFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveFamilyIdAsync(cancellationToken);
        return await _client.GetProductsByFamilyIdAsync(familyId, cancellationToken);
    }

    private async Task<int> ResolveFamilyIdAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio:family-id:{_options.ProductFamilyHandle}";
        if (_cache.TryGetValue<int>(cacheKey, out var cachedId))
        {
            return cachedId;
        }

        var families = await _client.GetProductFamiliesAsync(cancellationToken);
        var family = families.FirstOrDefault(f =>
            string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new BillingServiceException(
                $"No Maxio product family with handle '{_options.ProductFamilyHandle}' was found on this site. Check the Maxio:ProductFamilyHandle setting.");
        }

        _cache.Set(cacheKey, family.Id, FamilyCacheDuration);
        return family.Id;
    }

    private SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Id = product.Id,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = "USD",
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _options.ProductFamilyHandle
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        Currency = subscription.Currency ?? "USD",
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };

    // Deterministic token so genuine double-submits collide (and are reconciled), while distinct
    // operations do not. Maxio de-duplicates on this value within a 60-minute window.
    private static string UniquenessToken(params string[] parts) => string.Join(":", parts);
}
