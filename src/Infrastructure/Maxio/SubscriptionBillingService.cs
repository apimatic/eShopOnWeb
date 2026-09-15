using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "past_due",
        "soft_failure",
        "unpaid",
        "on_hold",
        "suspended",
        "awaiting_signup"
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _shopperLocks = new(StringComparer.Ordinal);
    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;

    public SubscriptionBillingService(IMaxioAdvancedBillingClient maxio, IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var products = await _maxio.ListProductsForProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && product.Id.HasValue && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        BillingShopper shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        var gate = _shopperLocks.GetOrAdd(shopper.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plan = await ResolvePlanAsync(productHandle, cancellationToken);
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id!.Value, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                return new SubscribeResult(MapSubscription(existing), Created: false);
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(new CreateSubscriptionPayload
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = SubscriptionReference(shopper.Id, plan.Handle),
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);

                return new SubscribeResult(MapSubscription(created), Created: true);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var raced = await FindLiveSubscriptionAsync(customer.Id!.Value, plan.Handle, cancellationToken);
                if (raced is not null)
                {
                    return new SubscribeResult(MapSubscription(raced), Created: false);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListShopperSubscriptionsAsync(
        string shopperId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var customer = await _maxio.ReadCustomerByReferenceAsync(shopperId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioNotConfiguredException();
        }
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var product = await _maxio.ReadProductByHandleAsync(productHandle, cancellationToken);
        if (product?.Id is null || string.IsNullOrWhiteSpace(product.Handle))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var familyHandle = product.ProductFamily?.Handle;
        if (!string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return MapPlan(product);
    }

    private async Task<CustomerPayload> EnsureCustomerAsync(BillingShopper shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.ReadCustomerByReferenceAsync(shopper.Id, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing;
        }

        var (firstName, lastName) = NameFromShopper(shopper);
        try
        {
            return await _maxio.CreateCustomerAsync(new CreateCustomerPayload
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Reference = shopper.Id
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.ReadCustomerByReferenceAsync(shopper.Id, cancellationToken);
            if (raced?.Id is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<SubscriptionPayload?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
            && IsLive(subscription.State));
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveSubscriptionStates.Contains(state);

    private static string SubscriptionReference(string shopperId, string productHandle) =>
        $"{shopperId}:{productHandle}";

    private static (string FirstName, string LastName) NameFromShopper(BillingShopper shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName! : shopper.Email;
        var local = source.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Humanize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Humanize(parts[^1]) : "eShopOnWeb";
        return (first, last);
    }

    private static string Humanize(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];

    private static SubscriptionPlan MapPlan(ProductPayload product) =>
        new(
            product.Id!.Value,
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            ToMoney(product.PriceInCents),
            product.PriceInCents ?? 0,
            product.Interval ?? 1,
            product.IntervalUnit ?? "month");

    private static ShopperSubscription MapSubscription(SubscriptionPayload subscription) =>
        new(
            subscription.Id!.Value,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? subscription.Product?.Handle ?? "Unknown plan",
            ToMoney(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            subscription.State ?? "unknown",
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static decimal ToMoney(long? cents) => (cents ?? 0) / 100m;
}
