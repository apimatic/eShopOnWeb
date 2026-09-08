using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "awaiting_signup"
    };

    private readonly IMaxioBillingClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioBillingClient client, IMemoryCache cache, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var currency = await GetSiteCurrencyAsync(cancellationToken);
        var products = await _client.ListProductsAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p, currency))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(string subscriberReference, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriberReference))
        {
            throw new MaxioApiException(400, "A subscriber reference is required.");
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioApiException(400, "A subscription plan handle is required.");
        }

        var currency = await GetSiteCurrencyAsync(cancellationToken);
        var products = await _client.ListProductsAsync(cancellationToken);

        var product = products.FirstOrDefault(p =>
            p.ArchivedAt is null &&
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

        if (product is null)
        {
            throw new MaxioApiException(404, $"No subscription plan with handle '{planHandle}' is available on this site.");
        }

        var customer = await EnsureCustomerAsync(subscriberReference, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer, product.Handle!, cancellationToken);
        if (existing is not null)
        {
            return new SubscriptionEnrollment
            {
                Subscription = MapSubscription(existing, currency),
                Created = false
            };
        }

        var subscription = new MaxioSubscriptionWrite
        {
            CustomerReference = customer.Reference ?? subscriberReference,
            ProductHandle = product.Handle,
            Reference = $"{subscriberReference}:{product.Handle}",
            PaymentCollectionMethod = "remittance"
        };

        var uniquenessToken = DeterministicToken("eshop-subscription", subscriberReference, product.Handle!);
        var created = await CreateSubscriptionWithRecoveryAsync(subscription, uniquenessToken, customer, product.Handle!, cancellationToken);

        return new SubscriptionEnrollment
        {
            Subscription = MapSubscription(created, currency),
            Created = true
        };
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string subscriberReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriberReference))
        {
            return Array.Empty<SubscriptionDto>();
        }

        var customer = await _client.FindCustomerByReferenceAsync(subscriberReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var currency = await GetSiteCurrencyAsync(cancellationToken);
        var subscriptions = await _client.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(s => MapSubscription(s, currency))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var write = new MaxioCustomerWrite
        {
            FirstName = DeriveFirstName(reference),
            LastName = "eShopOnWeb",
            Email = reference,
            Reference = reference
        };

        try
        {
            return await _client.CreateCustomerAsync(write, DeterministicToken("eshop-customer", reference), cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
        {
            _logger.LogWarning(ex, "Creating Maxio customer for reference {Reference} reported a conflict; re-reading the customer.", reference);

            var createdByOtherRequest = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (createdByOtherRequest is not null)
            {
                return createdByOtherRequest;
            }

            return await _client.CreateCustomerAsync(write, null, cancellationToken);
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionWithRecoveryAsync(
        MaxioSubscriptionWrite subscription,
        string uniquenessToken,
        MaxioCustomer customer,
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.CreateSubscriptionAsync(subscription, uniquenessToken, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 409)
        {
            _logger.LogWarning(ex, "Creating Maxio subscription for customer {CustomerId} reported a duplicate; re-reading subscriptions.", customer.Id);

            var createdByOtherRequest = await FindLiveSubscriptionAsync(customer, productHandle, cancellationToken);
            if (createdByOtherRequest is not null)
            {
                return createdByOtherRequest;
            }

            return await _client.CreateSubscriptionAsync(subscription, null, cancellationToken);
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(MaxioCustomer customer, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);

        return subscriptions
            .Where(s => IsLive(s.State) &&
                        string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Id)
            .FirstOrDefault();
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "maxio:site-currency";

        if (_cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached ?? string.Empty;
        }

        var site = await _client.GetSiteAsync(cancellationToken);
        var currency = site.Currency ?? string.Empty;
        _cache.Set(cacheKey, currency, TimeSpan.FromMinutes(15));
        return currency;
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product, string currency)
    {
        var priceInCents = product.PriceInCents ?? 0;

        return new SubscriptionPlanDto
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? product.Handle ?? string.Empty,
            Description = product.Description,
            PriceInCents = priceInCents,
            Price = priceInCents / 100m,
            Currency = currency,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty
        };
    }

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription, string siteCurrency)
    {
        var priceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        var currency = !string.IsNullOrWhiteSpace(subscription.Currency) ? subscription.Currency : siteCurrency;

        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State ?? string.Empty,
            Plan = subscription.Product is null ? null : MapPlan(subscription.Product, currency),
            PriceInCents = priceInCents,
            Price = priceInCents / 100m,
            Currency = currency,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt,
            ExpiresAt = subscription.ExpiresAt
        };
    }

    private static bool IsLive(string? state) =>
        state is not null && LiveSubscriptionStates.Contains(state);

    private static string DeriveFirstName(string reference)
    {
        var localPart = reference.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? "Subscriber" : localPart;
    }

    private static string DeterministicToken(params string[] parts)
    {
        var input = Encoding.UTF8.GetBytes(string.Join("|", parts));
        using var md5 = MD5.Create();
        return new Guid(md5.ComputeHash(input)).ToString();
    }
}
