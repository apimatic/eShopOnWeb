using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Dtos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingGateway"/>.
/// </summary>
/// <remarks>
/// Everything is addressed by handle or by our own customer reference. Numeric catalog ids are
/// deliberately never persisted or hard-coded: a site re-seed reassigns them, handles survive.
/// </remarks>
public class MaxioBillingGateway : ISubscriptionBillingGateway
{
    private const int PageSize = 200;

    private readonly MaxioApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(MaxioApiClient client, IMemoryCache cache, ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    private MaxioSettings Settings => _client.Settings;

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _client.EnsureConfigured();

        var cacheKey = $"maxio:plans:{CacheScope()}";
        var cacheFor = TimeSpan.FromSeconds(Math.Max(0, Settings.CatalogCacheSeconds));

        if ((cacheFor > TimeSpan.Zero) && _cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && (cached is not null))
        {
            return cached;
        }

        var plans = await FetchPlansAsync(cancellationToken);

        if (cacheFor > TimeSpan.Zero)
        {
            _cache.Set(cacheKey, plans, cacheFor);
        }

        return plans;
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);

        return plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        _client.EnsureConfigured();

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        var envelope = await ExecuteAsync(() => _client.GetOrDefaultAsync<MaxioCustomerEnvelope>(path, cancellationToken));

        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        _client.EnsureConfigured();

        var payload = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerAttributes
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        try
        {
            var envelope = await _client.PostAsync<MaxioCustomerEnvelope>("customers.json", payload, safeToRetry: false, cancellationToken);

            return envelope.Customer is null
                ? throw new BillingProviderException("The billing provider returned a customer without a body.")
                : MapCustomer(envelope.Customer);
        }
        catch (MaxioApiException exception) when (IsReferenceTaken(exception))
        {
            // Someone else created this customer between our lookup and our create. The reference is
            // unique at the provider, so whoever won the race created exactly the record we wanted.
            _logger.LogInformation("Billing customer reference {Reference} was already taken; reusing the existing record.", customer.Reference);

            var existing = await FindCustomerByReferenceAsync(customer.Reference, cancellationToken);

            return existing ?? throw Translate(exception);
        }
        catch (MaxioApiException exception)
        {
            throw Translate(exception);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        _client.EnsureConfigured();

        var path = string.Format(CultureInfo.InvariantCulture, "customers/{0}/subscriptions.json", customerId);

        var envelopes = await ExecuteAsync(() => _client.GetAsync<List<MaxioSubscriptionEnvelope>>(path, cancellationToken));

        return envelopes
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription!))
            .OrderByDescending(subscription => subscription.CreatedAt)
            .ToList();
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default)
    {
        _client.EnsureConfigured();

        var payload = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = subscription.PlanHandle,
                CustomerId = subscription.CustomerId,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            },
            UniquenessToken = subscription.IdempotencyToken
        };

        // Retrying is safe precisely because the payload carries the uniqueness token.
        var envelope = await ExecuteAsync(() =>
            _client.PostAsync<MaxioSubscriptionEnvelope>("subscriptions.json", payload, safeToRetry: true, cancellationToken));

        return envelope.Subscription is null
            ? throw new BillingProviderException("The billing provider returned a subscription without a body.")
            : MapSubscription(envelope.Subscription);
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> FetchPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = Settings.ProductFamilyHandle!.Trim();
        var currency = await GetSiteCurrencyAsync(cancellationToken);

        var products = new List<MaxioProduct>();

        for (var page = 1; ; page++)
        {
            var path = string.Format(
                CultureInfo.InvariantCulture,
                "product_families/handle:{0}/products.json?per_page={1}&page={2}",
                Uri.EscapeDataString(familyHandle),
                PageSize,
                page);

            var envelopes = await ExecuteAsync(() => _client.GetAsync<List<MaxioProductEnvelope>>(path, cancellationToken));

            products.AddRange(envelopes.Select(envelope => envelope.Product).Where(product => product is not null)!);

            if (envelopes.Count < PageSize)
            {
                break;
            }
        }

        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle))
            .Where(product => product.ArchivedAt is null)
            .Select(product => MapPlan(product, familyHandle, currency))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Products carry a price but not a currency; the site does. Cached because it effectively
    /// never changes and the provider limits how much we may ask of it at once.
    /// </summary>
    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio:site-currency:{CacheScope()}";

        if (_cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached!;
        }

        var envelope = await ExecuteAsync(() => _client.GetAsync<MaxioSiteEnvelope>("site.json", cancellationToken));
        var currency = envelope.Site?.Currency;

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new BillingProviderException("The billing site did not report a currency.");
        }

        _cache.Set(cacheKey, currency, TimeSpan.FromMinutes(10));

        return currency!;
    }

    /// <summary>
    /// Scopes cache entries to the site and family they came from, so pointing the same process at
    /// a different billing site or catalog can never serve it another one's plans. Built from the
    /// raw settings rather than the resolved address so it is safe to call before validation.
    /// </summary>
    private string CacheScope() =>
        $"{Settings.BaseUrl ?? Settings.Subdomain}:{Settings.ProductFamilyHandle}";

    private static SubscriptionPlan MapPlan(MaxioProduct product, string familyHandle, string currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        PaymentMethodRequired = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? familyHandle
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
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency ?? string.Empty,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod
    };

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (MaxioApiException exception)
        {
            throw Translate(exception);
        }
    }

    private static bool IsReferenceTaken(MaxioApiException exception) =>
        (exception.StatusCode == 422) &&
        exception.Errors.Any(error => error.Contains("Reference", StringComparison.OrdinalIgnoreCase) &&
                                      error.Contains("unique", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Turns a provider response into something the application layer can act on. Nothing that
    /// mentions Maxio, HTTP or credentials escapes past this point.
    /// </summary>
    private static Exception Translate(MaxioApiException exception) => exception.StatusCode switch
    {
        401 or 403 => new BillingProviderException(
            "The billing provider rejected our credentials.",
            exception.StatusCode,
            innerException: exception),

        409 => new ConcurrentSubscribeException(
            "An identical subscribe request is already being processed by the billing provider.",
            exception),

        422 => new BillingValidationException(
            "The billing provider rejected the request.",
            exception.Errors,
            exception),

        429 => new BillingProviderException(
            "The billing provider is throttling requests. Please try again shortly.",
            exception.StatusCode,
            exception.Errors,
            exception),

        _ => new BillingProviderException(
            "The billing provider returned an unexpected response.",
            exception.StatusCode,
            exception.Errors,
            exception)
    };
}
