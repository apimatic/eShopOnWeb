using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : ISubscriptionService
{
    public const string CustomerReferencePrefix = "eshop-user:";

    private const int LockStripes = 64;

    private static readonly SemaphoreSlim[] UserLocks = Enumerable.Range(0, LockStripes)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "purged"
    };

    private readonly IMaxioApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IMemoryCache cache,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = $"maxio-plans:{_options.ProductFamilyHandle}";
        if (_options.PlanCacheSeconds > 0 && _cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached != null)
        {
            return cached;
        }

        var plans = (await LoadActiveProductsAsync(cancellationToken)).Select(ToPlan).ToList();

        if (_options.PlanCacheSeconds > 0)
        {
            _cache.Set(cacheKey, (IReadOnlyList<SubscriptionPlan>)plans, TimeSpan.FromSeconds(_options.PlanCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        ValidateSubscriber(subscriber);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingProviderException(BillingFailureKind.Rejected, "A plan handle is required.");
        }

        planHandle = planHandle.Trim();

        var product = (await LoadActiveProductsAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var gate = UserLocks[(uint)StringComparer.Ordinal.GetHashCode(subscriber.UserId) % LockStripes];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            var existing = (await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .Where(IsInConfiguredFamily)
                .FirstOrDefault(s => !IsTerminal(s)
                                     && string.Equals(s.Product?.Handle, product.Handle, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _logger.LogInformation("User already has subscription {SubscriptionId} on plan {Plan}; returning it", existing.Id, product.Handle);
                return new SubscribeResult(ToDetails(existing, customer.Id), false);
            }

            var created = await _client.CreateSubscriptionAsync(new MaxioNewSubscription
            {
                CustomerId = customer.Id,
                ProductHandle = product.Handle!,
                PaymentCollectionMethod = _options.EffectivePaymentCollectionMethod
            }, cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} on plan {Plan} for customer {CustomerId}", created.Id, product.Handle, customer.Id);
            return new SubscribeResult(ToDetails(created, customer.Id), true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ValidateSubscriber(subscriber);

        var customer = await _client.FindCustomerByReferenceAsync(ReferenceFor(subscriber), cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        return (await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .Where(IsInConfiguredFamily)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => ToDetails(s, customer.Id))
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var reference = ReferenceFor(subscriber);
        var customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer != null)
        {
            return customer;
        }

        var (first, last) = NamesFor(subscriber);
        try
        {
            return await _client.CreateCustomerAsync(new MaxioNewCustomer
            {
                FirstName = first,
                LastName = last,
                Email = subscriber.Email,
                Reference = reference
            }, cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.Kind == BillingFailureKind.Rejected)
        {
            var raced = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<List<MaxioProduct>> LoadActiveProductsAsync(CancellationToken cancellationToken)
    {
        var products = await _client.ListProductsAsync(_options.ProductFamilyHandle!, cancellationToken);
        return products
            .Where(p => p.ArchivedAt == null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool IsInConfiguredFamily(MaxioSubscription subscription)
    {
        var family = subscription.Product?.ProductFamily?.Handle;
        return family != null && string.Equals(family, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTerminal(MaxioSubscription subscription)
        => subscription.State != null && TerminalStates.Contains(subscription.State);

    private static string ReferenceFor(SubscriberIdentity subscriber) => CustomerReferencePrefix + subscriber.UserId;

    private static (string First, string Last) NamesFor(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName;
        if (string.IsNullOrWhiteSpace(first))
        {
            var at = subscriber.Email.IndexOf('@');
            first = at > 0 ? subscriber.Email[..at] : subscriber.Email;
        }

        var last = string.IsNullOrWhiteSpace(subscriber.LastName) ? "Shopper" : subscriber.LastName;
        return (first.Trim(), last.Trim());
    }

    private static void ValidateSubscriber(SubscriberIdentity subscriber)
    {
        if (string.IsNullOrWhiteSpace(subscriber.UserId))
        {
            throw new ArgumentException("A subscriber user id is required.", nameof(subscriber));
        }
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        product.Handle!,
        product.Name ?? product.Handle!,
        product.Description,
        product.PriceInCents / 100m,
        product.Interval,
        product.IntervalUnit ?? string.Empty);

    private static SubscriptionDetails ToDetails(MaxioSubscription s, long customerId) => new(
        s.Id,
        s.Customer?.Id ?? customerId,
        s.State ?? "unknown",
        s.Product?.Handle ?? string.Empty,
        s.Product?.Name ?? s.Product?.Handle ?? string.Empty,
        (s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0) / 100m,
        s.Currency,
        s.Product?.Interval ?? 0,
        s.Product?.IntervalUnit ?? string.Empty,
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        s.CreatedAt,
        s.PaymentCollectionMethod);
}
