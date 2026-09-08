using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public class SubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly string[] ActiveSubscriptionStates = { "active", "trialing", "on_hold", "paused", "unpaid", "past_due" };

    private readonly MaxioApiClient _apiClient;
    private readonly MaxioSettings _settings;
    private readonly object _catalogLock = new object();
    private CachedCatalog? _cachedCatalog;

    public SubscriptionService(MaxioApiClient apiClient, MaxioSettings settings)
    {
        _apiClient = apiClient;
        _settings = settings;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog.Plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriptionSubscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var catalog = await GetCatalogAsync(cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customer = await GetOrCreateCustomerAsync(subscriber, cancellationToken);

        var existingSubscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions
            .Where(s => string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            .Where(s => ActiveSubscriptionStates.Contains(s.State, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();

        if (existing != null)
        {
            return new SubscribeResult(ToSubscription(existing, plan.Currency), created: false);
        }

        var created = await _apiClient.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = plan.Handle,
            CustomerId = customer.Id,
            PaymentCollectionMethod = "remittance"
        }, cancellationToken);

        return new SubscribeResult(ToSubscription(created, plan.Currency), created: true);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string subscriberReference, CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var customer = await _apiClient.LookupCustomerByReferenceAsync(subscriberReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var currency = await GetSiteCurrencyAsync(cancellationToken);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => ToSubscription(s, currency))
            .ToList();
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(SubscriptionSubscriber subscriber, CancellationToken cancellationToken)
    {
        var existing = await _apiClient.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            return await _apiClient.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            var raced = await _apiClient.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<CachedCatalog> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var cached = _cachedCatalog;
        if (cached != null && cached.ValidUntilUtc > DateTime.UtcNow)
        {
            return cached;
        }

        _settings.Validate();

        var families = await _apiClient.ListProductFamiliesAsync(cancellationToken);
        var family = families.FirstOrDefault(f => string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
        if (family == null)
        {
            throw new MaxioApiException(404, $"No Maxio product family with handle '{_settings.ProductFamilyHandle}' was found.");
        }

        var products = await _apiClient.ListProductsForFamilyAsync(family.Id, cancellationToken);
        var site = await _apiClient.GetSiteAsync(cancellationToken);

        var plans = products
            .Where(p => string.IsNullOrEmpty(p.ArchivedAt))
            .Select(p => new SubscriptionPlan
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = CentsToAmount(p.PriceInCents),
                Currency = site.Currency,
                Interval = p.Interval ?? 1,
                IntervalUnit = string.IsNullOrEmpty(p.IntervalUnit) ? "month" : p.IntervalUnit,
                PaymentMethodRequired = p.RequireCreditCard
            })
            .OrderBy(p => p.Price)
            .ToList();

        cached = new CachedCatalog
        {
            Plans = plans,
            Currency = site.Currency,
            ValidUntilUtc = DateTime.UtcNow.Add(CatalogCacheDuration)
        };

        lock (_catalogLock)
        {
            _cachedCatalog = cached;
        }

        return cached;
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog.Currency;
    }

    private static CustomerSubscription ToSubscription(MaxioSubscription subscription, string siteCurrency)
    {
        var priceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents;

        return new CustomerSubscription
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            Price = CentsToAmount(priceInCents),
            Currency = string.IsNullOrEmpty(subscription.Currency) ? siteCurrency : subscription.Currency,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? string.Empty,
            CreatedAt = subscription.CreatedAt ?? DateTimeOffset.MinValue,
            ActivatedAt = subscription.ActivatedAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }

    private static decimal CentsToAmount(int? cents)
    {
        return cents.HasValue ? Math.Round(cents.Value / 100m, 2) : 0m;
    }

    private sealed class CachedCatalog
    {
        public IReadOnlyList<SubscriptionPlan> Plans { get; init; } = Array.Empty<SubscriptionPlan>();
        public string Currency { get; init; } = "USD";
        public DateTime ValidUntilUtc { get; init; }
    }
}
