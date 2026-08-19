using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsForConfiguredFamilyAsync(cancellationToken);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string? productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new ArgumentException("A shopper user id is required.", nameof(shopper));
        }

        var handle = (productHandle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(handle))
        {
            var plans = await ListPlansAsync(cancellationToken);
            handle = plans.FirstOrDefault()?.Handle ?? string.Empty;
            if (string.IsNullOrWhiteSpace(handle))
            {
                throw new MaxioApiException(HttpStatusCode.BadRequest,
                    "No subscription plans are available to subscribe to.");
            }
        }

        var gateKey = $"{shopper.UserId}:{handle}";
        var gate = Gates.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation($"Returning existing Maxio subscription {existing.Id} for user {shopper.UserId} on plan {handle}.");
                return new SubscribeResult(existing, Created: false);
            }

            var uniquenessToken = Guid.NewGuid().ToString("N");
            try
            {
                var created = await _maxio.CreateSubscriptionAsync(customer.Id, handle, uniquenessToken, cancellationToken);
                _logger.LogInformation($"Created Maxio subscription {created.Id} for user {shopper.UserId} on plan {handle}.");
                return new SubscribeResult(created, Created: true);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                var raced = await FindLiveSubscriptionAsync(customer.Id, handle, cancellationToken)
                    ?? (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                        .OrderByDescending(s => s.Id)
                        .FirstOrDefault(s => string.Equals(s.ProductHandle, handle, StringComparison.OrdinalIgnoreCase));

                if (raced is null)
                {
                    throw;
                }

                return new SubscribeResult(raced, Created: false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<ShopperSubscription>();
        }

        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        var email = string.IsNullOrWhiteSpace(shopper.Email) ? $"{shopper.UserId}@eshop.local" : shopper.Email;

        try
        {
            return await _maxio.CreateCustomerAsync(
                new NewBillingCustomer(firstName, lastName, email, shopper.UserId),
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<ShopperSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            SubscriptionStates.IsLive(s.State) &&
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static (string FirstName, string LastName) SplitName(ShopperIdentity shopper)
    {
        var source = shopper.UserName ?? shopper.Email ?? shopper.UserId;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        var parts = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (parts[0], string.Join(" ", parts.Skip(1)));
        }

        return (string.IsNullOrWhiteSpace(local) ? "Shopper" : local, "eShopOnWeb");
    }
}
