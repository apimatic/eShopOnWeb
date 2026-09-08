using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

public class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();
    private static readonly HashSet<string> EndedSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly IMemoryCache _cache;

    public SubscriptionService(IMaxioClient maxioClient, IOptions<MaxioOptions> options, IMemoryCache cache)
    {
        _maxioClient = maxioClient;
        _options = options;
        _cache = cache;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        string familyHandle = FamilyHandle();
        var products = await _maxioClient.ListProductsForFamilyAsync(familyHandle, cancellationToken);
        string currencyCode = await GetSiteCurrencyAsync(cancellationToken);

        var plans = new List<SubscriptionPlanDto>();
        foreach (var product in products)
        {
            plans.Add(new SubscriptionPlanDto
            {
                Id = product.Id,
                Handle = product.Handle ?? string.Empty,
                Name = product.Name ?? string.Empty,
                Description = product.Description,
                Amount = ToAmount(product.PriceInCents),
                CurrencyCode = currencyCode,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit ?? string.Empty,
                InitialCharge = product.InitialChargeInCents.HasValue ? ToAmount(product.InitialChargeInCents.Value) : null,
                TrialInterval = product.TrialInterval,
                TrialIntervalUnit = product.TrialIntervalUnit
            });
        }

        return plans;
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(customerReference));
        }

        string familyHandle = FamilyHandle();
        string requestedPlanHandle = planHandle.Trim();

        var products = await _maxioClient.ListProductsForFamilyAsync(familyHandle, cancellationToken);
        var product = products.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.Handle) &&
            !p.ArchivedAt.HasValue &&
            string.Equals(p.Handle, requestedPlanHandle, StringComparison.OrdinalIgnoreCase));

        if (product is null || product.Handle is null)
        {
            throw new SubscriptionPlanNotFoundException(requestedPlanHandle);
        }

        var gate = SubscribeGates.GetOrAdd($"{customerReference}|{product.Handle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(customerReference, customerEmail, cancellationToken);
            var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existingSubscription = existingSubscriptions.FirstOrDefault(s =>
                IsSubscribedToPlan(s, product.Handle) && !IsEnded(s));

            if (existingSubscription is not null)
            {
                return new SubscriptionEnrollmentResult
                {
                    Subscription = ToSubscriptionDto(existingSubscription),
                    Created = false
                };
            }

            var createdSubscription = await _maxioClient.CreateSubscriptionAsync(product.Handle, customerReference, cancellationToken);
            return new SubscriptionEnrollmentResult
            {
                Subscription = ToSubscriptionDto(createdSubscription),
                Created = true
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string customerReference, CancellationToken cancellationToken)
    {
        string familyHandle = FamilyHandle();
        var customer = await _maxioClient.GetCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var results = new List<SubscriptionDto>();
        foreach (var subscription in subscriptions.OrderByDescending(s => s.CreatedAt))
        {
            if (subscription.Product?.ProductFamily is null ||
                !string.Equals(subscription.Product.ProductFamily.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(ToSubscriptionDto(subscription));
        }

        return results;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerReference, string customerEmail, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.GetCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveNameParts(customerEmail);
        try
        {
            return await _maxioClient.CreateCustomerAsync(customerReference, firstName, lastName, customerEmail, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            var concurrent = await _maxioClient.GetCustomerByReferenceAsync(customerReference, cancellationToken);
            if (concurrent is not null)
            {
                return concurrent;
            }

            throw;
        }
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "Maxio:Site:Currency";
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        var site = await _maxioClient.GetSiteAsync(cancellationToken);
        string currencyCode = site.Currency ?? string.Empty;
        _cache.Set(cacheKey, currencyCode, TimeSpan.FromHours(1));
        return currencyCode;
    }

    private static bool IsSubscribedToPlan(MaxioSubscription subscription, string productHandle)
        => subscription.Product is not null &&
           !string.IsNullOrEmpty(subscription.Product.Handle) &&
           string.Equals(subscription.Product.Handle, productHandle, StringComparison.OrdinalIgnoreCase);

    private static bool IsEnded(MaxioSubscription subscription)
        => subscription.State is not null && EndedSubscriptionStates.Contains(subscription.State);

    private SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        long priceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0;
        string currencyCode = subscription.Currency ?? string.Empty;

        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanId = product?.Id ?? 0,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            Amount = ToAmount(priceInCents),
            CurrencyCode = currencyCode,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
        };
    }

    private static decimal ToAmount(long priceInCents) => priceInCents / 100m;

    private static (string FirstName, string LastName) DeriveNameParts(string? email)
    {
        string localPart = email ?? string.Empty;
        int atIndex = localPart.IndexOf('@');
        if (atIndex > 0)
        {
            localPart = localPart.Substring(0, atIndex);
        }

        var parts = localPart.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("eShopUser", "-");
        }

        string firstName = parts[0];
        string lastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "-";
        return (firstName, lastName);
    }

    private string FamilyHandle()
    {
        string familyHandle = _options.Value.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioConfigurationException(
                "No Maxio product family is configured. Set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable (bound to the Maxio:ProductFamilyHandle configuration key).");
        }

        return familyHandle;
    }
}
