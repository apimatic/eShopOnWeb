using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new(StringComparer.Ordinal);

    private readonly MaxioApiClient _api;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioApiClient api,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _api = api;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var cacheKey = $"maxio:plans:{_options.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var products = await _api.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        var plans = products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .OrderBy(p => p.Price)
            .ToList();

        _cache.Set(cacheKey, (IReadOnlyList<SubscriptionPlan>)plans, TimeSpan.FromMinutes(2));
        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(shopper);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        productHandle = productHandle.Trim();

        var gate = UserGates.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeLockedAsync(shopper, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(shopper);

        var customer = await _api.FindCustomerByReferenceAsync(CustomerReference(shopper.UserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToCustomerSubscription).ToList();
    }

    private async Task<SubscribeResult> SubscribeLockedAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (plans.All(p => !string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PlanNotFoundException(productHandle);
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Returning existing Maxio subscription {SubscriptionId} for user {UserId} on plan {Plan}.",
                existing.Id,
                shopper.UserId,
                productHandle);
            return new SubscribeResult(ToCustomerSubscription(existing), Created: false);
        }

        var reference = SubscriptionReference(shopper.UserId, productHandle);
        var byReference = await _api.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (byReference is not null && IsLive(byReference.State))
        {
            return new SubscribeResult(ToCustomerSubscription(byReference), Created: false);
        }

        try
        {
            var created = await _api.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id,
                    Reference = reference,
                    PaymentCollectionMethod = "remittance"
                },
                Guid.NewGuid().ToString(),
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for user {UserId} on plan {Plan}.",
                created.Id,
                shopper.UserId,
                productHandle);

            return new SubscribeResult(ToCustomerSubscription(created), Created: true);
        }
        catch (MaxioConflictException)
        {
            _logger.LogInformation(
                "Maxio uniqueness token conflict for user {UserId} on plan {Plan}; loading existing subscription.",
                shopper.UserId,
                productHandle);
            return await RecoverSubscriptionAsync(customer.Id, productHandle, reference, cancellationToken);
        }
        catch (MaxioUnprocessableException ex) when (LooksLikeDuplicate(ex.Payload))
        {
            _logger.LogInformation(
                "Maxio rejected a duplicate subscription for user {UserId} on plan {Plan}; returning existing subscription.",
                shopper.UserId,
                productHandle);
            return await RecoverSubscriptionAsync(customer.Id, productHandle, reference, cancellationToken);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(shopper.UserId);
        var existing = await _api.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        try
        {
            return await _api.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = shopper.Email,
                    Reference = reference
                },
                cancellationToken);
        }
        catch (MaxioUnprocessableException)
        {
            var raced = await _api.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
            && IsLive(s.State));
    }

    private async Task<SubscribeResult> RecoverSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var byReference = await _api.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (byReference is not null)
        {
            return new SubscribeResult(ToCustomerSubscription(byReference), Created: false);
        }

        var live = await FindLiveSubscriptionAsync(customerId, productHandle, cancellationToken);
        if (live is not null)
        {
            return new SubscribeResult(ToCustomerSubscription(live), Created: false);
        }

        throw new BillingException(
            $"A duplicate subscribe request was detected for plan '{productHandle}', but the existing subscription could not be loaded.");
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle (and optionally Maxio:BaseUrl).");
        }
    }

    internal static string CustomerReference(string userId) => $"eshop:{userId}";

    internal static string SubscriptionReference(string userId, string productHandle) => $"eshop:{userId}:{productHandle}";

    internal static string UniquenessToken(string userId, string productHandle)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"maxio-subscribe:{userId}:{productHandle}"));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    internal static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !EndOfLifeStates.Contains(state);

    private static bool LooksLikeDuplicate(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        return payload.Contains("reference", StringComparison.OrdinalIgnoreCase)
               && (payload.Contains("taken", StringComparison.OrdinalIgnoreCase)
                   || payload.Contains("already", StringComparison.OrdinalIgnoreCase)
                   || payload.Contains("unique", StringComparison.OrdinalIgnoreCase)
                   || payload.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    private static (string FirstName, string LastName) SplitName(Shopper shopper)
    {
        var source = shopper.UserName;
        if (source.Contains('@', StringComparison.Ordinal))
        {
            source = source[..source.IndexOf('@')];
        }

        source = source.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            return ("eShop", "Subscriber");
        }

        var parts = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return (parts[0], "Subscriber");
        }

        return (parts[0], string.Join(' ', parts.Skip(1)));
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) =>
        new(
            product.Handle!,
            product.Name,
            product.Description,
            CentsToDecimal(product.PriceInCents),
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit ?? "month");

    internal static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription)
    {
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new CustomerSubscription(
            subscription.Id,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            CentsToDecimal(priceInCents),
            priceInCents,
            subscription.State ?? "unknown",
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodEndsAt);
    }

    internal static decimal CentsToDecimal(long cents) => cents / 100m;
}
