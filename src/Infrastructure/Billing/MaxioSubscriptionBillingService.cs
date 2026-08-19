using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "paused",
        "unpaid",
        "awaiting_signup"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsForProductFamilyAsync(RequireFamilyHandle(), cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        string buyerId,
        string email,
        string userName,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionPlanNotFoundException(productHandle ?? string.Empty);
        }

        var handle = productHandle.Trim();
        return await WithGateAsync($"subscribe:{buyerId}:{handle}", async () =>
        {
            await RequirePlanAsync(handle, cancellationToken);
            var customer = await EnsureCustomerAsync(buyerId, email, userName, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id!.Value, buyerId, handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation($"Returning existing Maxio subscription {existing.Id} for buyer {buyerId} on plan {handle}.");
                return new SubscribeResult { Subscription = MapSubscription(existing), Created = false };
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(new CreateSubscription
                {
                    ProductHandle = handle,
                    CustomerId = customer.Id,
                    Reference = BuildSubscriptionReference(buyerId, handle),
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);

                _logger.LogInformation($"Created Maxio subscription {created.Id} for buyer {buyerId} on plan {handle}.");
                return new SubscribeResult { Subscription = MapSubscription(created), Created = true };
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422)
            {
                var raced = await FindLiveSubscriptionAsync(customer.Id!.Value, buyerId, handle, cancellationToken);
                if (raced is not null)
                {
                    return new SubscribeResult { Subscription = MapSubscription(raced), Created = false };
                }

                throw;
            }
        });
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _maxio.ReadCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<Customer> EnsureCustomerAsync(
        string buyerId,
        string email,
        string userName,
        CancellationToken cancellationToken)
    {
        return await WithGateAsync($"customer:{buyerId}", async () =>
        {
            var existing = await _maxio.ReadCustomerByReferenceAsync(buyerId, cancellationToken);
            if (existing?.Id is not null)
            {
                return existing;
            }

            var (firstName, lastName) = ShopperName.FromUser(userName, email);
            try
            {
                var created = await _maxio.CreateCustomerAsync(new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = buyerId
                }, cancellationToken);

                _logger.LogInformation($"Created Maxio customer {created.Id} for buyer {buyerId}.");
                return created;
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422)
            {
                var raced = await _maxio.ReadCustomerByReferenceAsync(buyerId, cancellationToken);
                if (raced?.Id is not null)
                {
                    return raced;
                }

                throw;
            }
        });
    }

    private async Task<SubscriptionPlan> RequirePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(item => string.Equals(item.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return plan;
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(
        int customerId,
        string buyerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var live = subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription.State) &&
            string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        if (live is not null)
        {
            return live;
        }

        var byReference = await _maxio.FindSubscriptionByReferenceAsync(
            BuildSubscriptionReference(buyerId, productHandle),
            cancellationToken);

        if (byReference is not null &&
            IsLive(byReference.State) &&
            string.Equals(byReference.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
        {
            return byReference;
        }

        return null;
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);

    private string RequireFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle must be configured.");
        }

        return _options.ProductFamilyHandle.Trim();
    }

    private static string BuildSubscriptionReference(string buyerId, string productHandle) =>
        $"eshop:{buyerId}:{productHandle}";

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Id = product.Id ?? 0,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        Price = ToMoney(product.PriceInCents),
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static ShopperSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        Price = ToMoney(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
        State = subscription.State ?? string.Empty,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };

    private static decimal ToMoney(long? priceInCents) =>
        (priceInCents ?? 0) / 100m;

    private static async Task<T> WithGateAsync<T>(string key, Func<Task<T>> action)
    {
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
