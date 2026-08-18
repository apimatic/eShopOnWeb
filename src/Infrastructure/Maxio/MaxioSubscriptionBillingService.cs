using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioCreateCustomer = Microsoft.eShopWeb.Infrastructure.Maxio.Models.CreateCustomer;
using MaxioCreateSubscription = Microsoft.eShopWeb.Infrastructure.Maxio.Models.CreateSubscription;
using MaxioCustomer = Microsoft.eShopWeb.Infrastructure.Maxio.Models.Customer;
using MaxioProduct = Microsoft.eShopWeb.Infrastructure.Maxio.Models.Product;
using MaxioSubscription = Microsoft.eShopWeb.Infrastructure.Maxio.Models.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> ShopperLocks = new();

    private readonly IMaxioApiClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureProductFamilyConfigured();
        var products = await _maxio.ListProductsForProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt == null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        if (shopper == null) throw new ArgumentNullException(nameof(shopper));
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required to subscribe.", nameof(productHandle));
        }

        EnsureProductFamilyConfigured();

        var gate = ShopperLocks.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plan = await FindPlanAsync(productHandle, cancellationToken);
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var subscriptionReference = BuildSubscriptionReference(customer.Id, plan.Handle);

            var existing = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing != null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for shopper {ShopperId}", existing.Id, shopper.UserId);
                return new SubscribeResult(MapSubscription(existing), created: false);
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference,
                    // Spec Collection-Method + create example "Basic": remittance enrolls without a payment profile.
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);

                return new SubscribeResult(MapSubscription(created), created: true);
            }
            catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var raced = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (raced != null)
                {
                    return new SubscribeResult(MapSubscription(raced), created: false);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListShopperSubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken = default)
    {
        if (shopper == null) throw new ArgumentNullException(nameof(shopper));

        var customer = await FindCustomerAsync(shopper, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<SubscriptionPlan> FindPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return plan;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(shopper, cancellationToken);
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
                Email = shopper.Email,
                Reference = shopper.UserId
            }, cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await FindCustomerAsync(shopper, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var byReference = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (byReference != null)
        {
            return byReference;
        }

        if (string.IsNullOrWhiteSpace(shopper.Email))
        {
            return null;
        }

        var matches = await _maxio.ListCustomersAsync(shopper.Email, cancellationToken);
        return matches.FirstOrDefault(c => string.Equals(c.Email, shopper.Email, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureProductFamilyConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingConfigurationException("Maxio:ProductFamilyHandle is not configured.");
        }
    }

    internal static string BuildSubscriptionReference(int maxioCustomerId, string productHandle)
        => $"{maxioCustomerId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitName(Shopper shopper)
    {
        var source = shopper.Email;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : (string.IsNullOrWhiteSpace(shopper.UserName) ? "Shopper" : shopper.UserName);
        var parts = local.Split(new[] { '.', '_', '+', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(parts[1]) : "Customer";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlan(
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            CentsToDecimal(product.PriceInCents),
            product.Interval,
            product.IntervalUnit ?? "month");
    }

    private static ShopperSubscription MapSubscription(MaxioSubscription subscription)
    {
        var nextBilling = ParseTimestamp(subscription.NextAssessmentAt)
            ?? ParseTimestamp(subscription.CurrentPeriodEndsAt);

        return new ShopperSubscription(
            subscription.Id,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            CentsToDecimal(subscription.ProductPriceInCents),
            subscription.State ?? "unknown",
            nextBilling);
    }

    private static decimal CentsToDecimal(long cents) => cents / 100m;

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
