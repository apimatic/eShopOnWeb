using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollLocks = new(StringComparer.Ordinal);

    private static readonly HashSet<string> EndedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(shopper);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("A productHandle is required to subscribe.", 400);
        }

        productHandle = productHandle.Trim();
        var gate = EnrollLocks.GetOrAdd($"{shopper.Id}:{productHandle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plan = await RequirePlanAsync(productHandle, cancellationToken);
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, shopper.Id, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for shopper {ShopperId} plan {Plan}",
                    existing.Id, shopper.Id, productHandle);
                return MapSubscription(existing, alreadyExisted: true);
            }

            var created = await CreateSubscriptionIdempotentAsync(customer.Id, shopper.Id, plan.Handle, cancellationToken);
            return MapSubscription(created, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(shopper);

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.Id, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => MapSubscription(subscription, alreadyExisted: true)).ToList();
    }

    private async Task<SubscriptionPlan> RequirePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var product = await _maxio.GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null || product.ArchivedAt is not null)
        {
            throw new UnknownSubscriptionPlanException(productHandle);
        }

        var familyHandle = product.ProductFamily?.Handle;
        if (!string.IsNullOrWhiteSpace(familyHandle)
            && !string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnknownSubscriptionPlanException(productHandle);
        }

        return MapPlan(product);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Reference = shopper.Id
            }, cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode is 400 or 409)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.Id, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        string shopperId,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(
            SubscriptionReference(shopperId, productHandle), cancellationToken);
        if (byReference is not null && IsLive(byReference) && MatchesProduct(byReference, productHandle))
        {
            return byReference;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription) && MatchesProduct(subscription, productHandle));
    }

    private async Task<MaxioSubscription> CreateSubscriptionIdempotentAsync(
        int customerId,
        string shopperId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var reference = SubscriptionReference(shopperId, productHandle);
        try
        {
            return await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = reference
            }, cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode is 400 or 409)
        {
            var existing = await FindLiveSubscriptionAsync(customerId, productHandle, shopperId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new BillingConfigurationException(
                "Maxio:ApiKey is required. Set user-secret Maxio:ApiKey or environment variable MAXIO_API_KEY.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                "Maxio:ProductFamilyHandle is required. Set user-secret Maxio:ProductFamilyHandle or environment variable MAXIO_DEFAULT_PRODUCT_FAMILY.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new BillingConfigurationException(
                "Maxio:Subdomain or Maxio:BaseUrl is required. Set Maxio:Subdomain / MAXIO_SITE_SUBDOMAIN, or Maxio:BaseUrl.");
        }
    }

    private static string SubscriptionReference(string shopperId, string productHandle) =>
        $"{shopperId}:{productHandle}";

    private static bool IsLive(MaxioSubscription subscription) =>
        !string.IsNullOrWhiteSpace(subscription.State) && !EndedStates.Contains(subscription.State);

    private static bool MatchesProduct(MaxioSubscription subscription, string productHandle) =>
        string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase);

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static ShopperSubscription MapSubscription(MaxioSubscription subscription, bool alreadyExisted) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        AlreadyExisted = alreadyExisted
    };

    internal static (string FirstName, string LastName) SplitName(Shopper shopper)
    {
        var source = shopper.UserName;
        var at = source.IndexOf('@');
        if (at > 0)
        {
            source = source[..at];
        }

        var parts = source.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "Customer";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}
