using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioBillingClient _maxio;
    private readonly ISubscriptionIdempotencyLock _gate;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioBillingClient maxio,
        ISubscriptionIdempotencyLock gate,
        IAppLogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _gate = gate;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var plans = await _maxio.ListPlansAsync(cancellationToken);
        return plans.Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle)).ToList();
    }

    public Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        Guard.Against.Null(shopper);
        Guard.Against.NullOrEmpty(shopper.UserId);
        Guard.Against.NullOrEmpty(shopper.Email);
        Guard.Against.NullOrEmpty(productHandle);

        var key = $"{shopper.UserId}:{productHandle}";
        return _gate.ExecuteAsync(key, () => SubscribeCoreAsync(shopper, productHandle, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        Guard.Against.Null(shopper);
        Guard.Against.NullOrEmpty(shopper.UserId);

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return System.Array.Empty<BillingSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, System.StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var existing = await FindCurrentSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Returning existing Maxio subscription {0} for user {1} on plan {2}",
                existing.Id, shopper.UserId, plan.Handle);
            return new SubscribeResult { Subscription = existing, Created = false };
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(
                customer.Id,
                plan.Handle,
                $"eshop-sub:{shopper.UserId}:{plan.Handle}:{System.Guid.NewGuid():N}",
                plan.RequireCreditCard,
                cancellationToken);

            _logger.LogInformation("Created Maxio subscription {0} for user {1} on plan {2}",
                created.Id, shopper.UserId, plan.Handle);
            return new SubscribeResult { Subscription = created, Created = true };
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
        {
            var raced = await FindCurrentSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (raced is not null)
            {
                return new SubscribeResult { Subscription = raced, Created = false };
            }

            throw;
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _maxio.CreateCustomerAsync(
                shopper,
                $"eshop-customer:{shopper.UserId}",
                cancellationToken);
            _logger.LogInformation("Created Maxio customer {0} for user {1}", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<BillingSubscription?> FindCurrentSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            s.IsCurrent &&
            string.Equals(s.ProductHandle, productHandle, System.StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureConfigured()
    {
        if (!_maxio.IsConfigured)
        {
            throw new MaxioNotConfiguredException();
        }
    }
}
