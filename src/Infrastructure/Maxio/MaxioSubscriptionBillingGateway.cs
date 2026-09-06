using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Adapts Maxio Advanced Billing to <see cref="ISubscriptionBillingGateway"/>: translates between the
/// wire contracts and eShopOnWeb's own subscription model, and turns Maxio's error responses into the
/// application's billing exceptions.
/// </summary>
public class MaxioSubscriptionBillingGateway : ISubscriptionBillingGateway
{
    private const string PlansCacheKey = "maxio:plans";
    private const string SiteCacheKey = "maxio:site";

    /// <summary>
    /// Invoice-based collection methods from <c>Collection-Method.yaml</c>. Either lets a subscription
    /// start without a payment profile; which one is valid depends on whether the site runs
    /// Relationship Invoicing.
    /// </summary>
    private const string RemittanceCollection = "remittance";
    private const string InvoiceCollection = "invoice";

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingGateway> _logger;

    public MaxioSubscriptionBillingGateway(IMaxioApiClient client, IOptionsMonitor<MaxioOptions> options,
        IMemoryCache cache, ILogger<MaxioSubscriptionBillingGateway> logger)
    {
        _client = client;
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var familyHandle = RequireProductFamilyHandle(options);

        if (_cache.TryGetValue<IReadOnlyList<SubscriptionPlan>>(PlansCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var currency = await GetSiteCurrencyAsync(cancellationToken);
        var products = await ExecuteAsync(
            () => _client.ListProductsForProductFamilyAsync(familyHandle, cancellationToken),
            $"list plans for product family '{familyHandle}'");

        var plans = products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p, currency))
            .OrderBy(p => p.PriceInCents)
            .ToList();

        if (plans.Count == 0)
        {
            _logger.LogWarning("Maxio product family '{ProductFamilyHandle}' exposes no subscribable plans.", familyHandle);
        }

        Cache(PlansCacheKey, (IReadOnlyList<SubscriptionPlan>)plans, options);
        return plans;
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var customer = await ExecuteAsync(
            () => _client.ReadCustomerByReferenceAsync(reference, cancellationToken),
            "look up the billing customer");
        return customer is null ? null : MapCustomer(customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var created = await ExecuteAsync(
            () => _client.CreateCustomerAsync(request, cancellationToken),
            "create the billing customer",
            customer.Reference);
        return MapCustomer(created);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken),
            "list the customer's subscriptions");
        return subscriptions.Select(MapSubscription).ToList();
    }

    public async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        var subscription = await ExecuteAsync(
            () => _client.FindSubscriptionAsync(reference, cancellationToken),
            "look up the subscription");
        return subscription is null ? null : MapSubscription(subscription);
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = subscription.PlanHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference,
                PaymentCollectionMethod = await ResolvePaymentCollectionMethodAsync(cancellationToken)
            }
        };

        var created = await ExecuteAsync(
            () => _client.CreateSubscriptionAsync(request, cancellationToken),
            "create the subscription",
            subscription.Reference);
        return MapSubscription(created);
    }

    /// <summary>
    /// Chooses how Maxio should collect payment. eShopOnWeb never captures a card, so signup has to
    /// use an invoice-based method: <c>remittance</c> on Relationship Invoicing sites, <c>invoice</c>
    /// on legacy Statements sites. A site that needs something else can pin it in configuration.
    /// </summary>
    private async Task<string> ResolvePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var configured = _options.CurrentValue.PaymentCollectionMethod;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!.Trim().ToLowerInvariant();
        }

        var site = await GetSiteAsync(cancellationToken);
        return site.RelationshipInvoicingEnabled ? RemittanceCollection : InvoiceCollection;
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        var site = await GetSiteAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(site.Currency) ? "USD" : site.Currency!;
    }

    private async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<MaxioSite>(SiteCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var site = await ExecuteAsync(() => _client.ReadSiteAsync(cancellationToken), "read the Maxio site settings");
        Cache(SiteCacheKey, site, _options.CurrentValue);
        return site;
    }

    private void Cache<T>(string key, T value, MaxioOptions options)
    {
        if (options.CatalogCacheSeconds <= 0)
        {
            return;
        }

        _cache.Set(key, value, TimeSpan.FromSeconds(options.CatalogCacheSeconds));
    }

    private static string RequireProductFamilyHandle(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                $"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.ProductFamilyHandle)} is not configured.");
        }

        return options.ProductFamilyHandle!.Trim();
    }

    /// <summary>
    /// Runs a Maxio call and re-expresses its failures in the application's vocabulary, so callers
    /// never have to know about HTTP status codes.
    /// </summary>
    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string description, string? reference = null)
    {
        try
        {
            return await operation();
        }
        catch (MaxioApiException ex) when (ex.IsReferenceConflict && reference is not null)
        {
            throw new BillingReferenceConflictException(reference, ex.Errors);
        }
        catch (MaxioApiException ex)
        {
            throw new BillingProviderException($"The billing system could not {description}.", ex.Errors, ex);
        }
        catch (MaxioTransportException ex)
        {
            throw new BillingProviderException($"The billing system was unreachable and could not {description}.",
                new[] { ex.Message }, ex);
        }
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product, string currency) => new()
    {
        Id = product.Id,
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        ProductFamilyHandle = product.ProductFamily?.Handle,
        RequiresPaymentMethod = product.RequireCreditCard,
        SetupFeeInCents = product.InitialChargeInCents,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents
    };

    private static BillingCustomer MapCustomer(MaxioCustomer customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        Reference = subscription.Reference,
        CustomerId = subscription.Customer?.Id ?? 0,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        TrialEndedAt = subscription.TrialEndedAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod
    };
}
