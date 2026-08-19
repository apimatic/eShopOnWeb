using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "past_due",
        "unpaid",
        "on_hold",
        "pending",
        "assessing",
        "paused",
        "soft_failure",
        "pending_cancellation"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ShopperLocks = new();

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
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Handle) && p.ArchivedAt == null)
            .Select(MapPlan)
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (shopper == null) throw new ArgumentNullException(nameof(shopper));
        if (string.IsNullOrWhiteSpace(shopper.UserId)) throw new ArgumentException("Shopper user id is required.", nameof(shopper));
        if (string.IsNullOrWhiteSpace(productHandle)) throw new ArgumentException("A product handle is required.", nameof(productHandle));

        var handle = productHandle.Trim();
        var plans = await ListAvailablePlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PlanNotFoundException(handle);
        }

        var gate = ShopperLocks.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(shopper, handle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (shopper == null) throw new ArgumentNullException(nameof(shopper));
        if (string.IsNullOrWhiteSpace(shopper.UserId)) throw new ArgumentException("Shopper user id is required.", nameof(shopper));

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<CustomerSubscription> SubscribeCoreAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptionReference = BuildSubscriptionReference(shopper.UserId, productHandle);

        var existingByReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existingByReference != null)
        {
            _logger.LogInformation(
                "Returning existing Maxio subscription {SubscriptionId} for shopper {UserId} plan {Handle}.",
                existingByReference.Id, shopper.UserId, productHandle);
            return MapSubscription(existingByReference);
        }

        var customer = await GetOrCreateCustomerAsync(shopper, cancellationToken);

        var customerSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var liveMatch = customerSubscriptions.FirstOrDefault(s =>
            s.Product != null &&
            string.Equals(s.Product.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            IsLive(s.State));

        if (liveMatch != null)
        {
            _logger.LogInformation(
                "Shopper {UserId} already has live Maxio subscription {SubscriptionId} for {Handle}.",
                shopper.UserId, liveMatch.Id, productHandle);
            return MapSubscription(liveMatch);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = "remittance",
                Reference = subscriptionReference
            }, cancellationToken);

            return MapSubscription(created);
        }
        catch (BillingGatewayException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (raced != null)
            {
                return MapSubscription(raced);
            }

            throw;
        }
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing != null)
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
                Email = string.IsNullOrWhiteSpace(shopper.Email) ? $"{shopper.UserId}@users.eshoponweb.local" : shopper.Email,
                Organization = "eShopOnWeb",
                Reference = shopper.UserId
            }, cancellationToken);
        }
        catch (BillingGatewayException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private void EnsureConfigured()
    {
        if (_options.IsConfigured)
        {
            return;
        }

        throw new BillingGatewayException(
            "Maxio Advanced Billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.",
            503);
    }

    private static bool IsLive(string state)
        => !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);

    private static string BuildSubscriptionReference(string userId, string productHandle)
        => $"eshop:{userId}:{productHandle}";

    private static SubscriptionPlan MapPlan(MaxioProduct product)
        => new(
            product.Handle,
            product.Name ?? product.Handle,
            product.Description ?? string.Empty,
            CentsToDecimal(product.PriceInCents),
            product.Interval <= 0 ? 1 : product.Interval,
            string.IsNullOrWhiteSpace(product.IntervalUnit) ? "month" : product.IntervalUnit);

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        var cents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0;
        var nextBilling = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;

        return new CustomerSubscription(
            subscription.Id,
            product?.Handle ?? string.Empty,
            product?.Name ?? product?.Handle ?? string.Empty,
            CentsToDecimal(cents),
            subscription.State ?? string.Empty,
            nextBilling);
    }

    private static decimal CentsToDecimal(long cents) => cents / 100m;

    private static (string FirstName, string LastName) SplitName(Shopper shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName)
            ? shopper.UserName
            : shopper.Email;

        if (string.IsNullOrWhiteSpace(source))
        {
            return ("Shopper", "eShopOnWeb");
        }

        var local = source.Contains('@') ? source.Split('@')[0] : source;
        var separators = new[] { '.', '_', '-', ' ' };
        var parts = local.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("Shopper", "eShopOnWeb");
        }

        if (parts.Length == 1)
        {
            return (parts[0], "Subscriber");
        }

        return (parts[0], string.Join(" ", parts.Skip(1)));
    }
}

