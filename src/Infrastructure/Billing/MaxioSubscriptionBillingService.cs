using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "assessing", "past_due", "soft_failure", "unpaid", "paused", "awaiting_signup"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerGates = new(StringComparer.Ordinal);

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

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToShopperSubscription).ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        var plans = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new PlanNotFoundException(productHandle);
        }

        var gate = CustomerGates.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle!, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for user {UserId} and plan {Handle}.",
                    existing.Id, shopper.UserId, plan.Handle);
                return ToShopperSubscription(existing);
            }

            var uniquenessToken = Guid.NewGuid().ToString("N");
            try
            {
                var created = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle!, uniquenessToken, cancellationToken);
                return ToShopperSubscription(created);
            }
            catch (MaxioDuplicateSubmissionException)
            {
                var replayed = await FindLiveSubscriptionAsync(customer.Id, plan.Handle!, cancellationToken);
                if (replayed is not null)
                {
                    return ToShopperSubscription(replayed);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        var uniquenessToken = $"customer:{shopper.UserId}";
        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCustomerPayload
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = shopper.Email,
                    Reference = shopper.UserId
                },
                uniquenessToken,
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
        catch (MaxioDuplicateSubmissionException)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(s.State)
            && LiveStates.Contains(s.State));
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product)
    {
        return new SubscriptionPlan(
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            product.PriceInCents / 100m,
            product.Interval,
            product.IntervalUnit ?? "month");
    }

    private static ShopperSubscription ToShopperSubscription(MaxioSubscription subscription)
    {
        var handle = subscription.Product?.Handle ?? string.Empty;
        var name = subscription.Product?.Name ?? handle;
        var priceCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new ShopperSubscription(
            subscription.Id,
            handle,
            name,
            priceCents / 100m,
            subscription.State ?? "unknown",
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static (string FirstName, string LastName) SplitName(ShopperIdentity shopper)
    {
        var source = shopper.UserName ?? shopper.Email;
        var local = source.Split('@')[0];
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        return (local, "eShopOnWeb");
    }
}
