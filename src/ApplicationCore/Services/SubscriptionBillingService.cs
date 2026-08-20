using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioBillingGateway _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;
    private readonly MaxioOptions _options;

    public SubscriptionBillingService(
        IMaxioBillingGateway maxio,
        IAppLogger<SubscriptionBillingService> logger,
        IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string? productHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var handle = (productHandle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingValidationException("productHandle is required.");
        }

        var product = await _maxio.GetProductByHandleAsync(handle, cancellationToken);
        if (product is null || product.ArchivedAt is not null)
        {
            throw new BillingValidationException($"Unknown subscription plan '{handle}'.");
        }

        if (!string.Equals(product.ProductFamilyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingValidationException($"Unknown subscription plan '{handle}'.");
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var subscriptionReference = BuildSubscriptionReference(shopper.UserId, handle);

        var existing = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null && SubscriptionStates.IsOpen(existing.State))
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.", existing.Id, shopper.UserId, handle);
            return new SubscribeResult { Subscription = ToShopperSubscription(existing, product), Created = false };
        }

        if (existing is not null)
        {
            subscriptionReference = $"{subscriptionReference}:{Guid.NewGuid():N}";
        }

        var uniquenessToken = Guid.NewGuid().ToString("D");

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(handle, customer.Id, subscriptionReference, uniquenessToken, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.", created.Id, shopper.UserId, handle);
            return new SubscribeResult { Subscription = ToShopperSubscription(created, product), Created = true };
        }
        catch (MaxioApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var recovered = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogInformation("Recovered Maxio subscription {SubscriptionId} after a conflict for user {UserId} plan {Plan}.", recovered.Id, shopper.UserId, handle);
                return new SubscribeResult { Subscription = ToShopperSubscription(recovered, product), Created = false };
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(subscription => subscription.CreatedAt)
            .Select(subscription => ToShopperSubscription(subscription, subscription.Product))
            .ToList();
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        try
        {
            var created = await _maxio.CreateCustomerAsync(firstName, lastName, shopper.Email, shopper.UserId, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioConfigurationException("Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"eshop:{userId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitName(ShopperIdentity shopper)
    {
        var source = shopper.Email;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : (string.IsNullOrWhiteSpace(shopper.UserName) ? "Shopper" : shopper.UserName);
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = parts.Length > 0 ? parts[0] : "Shopper";
        var lastName = parts.Length > 1 ? parts[1] : "eShopOnWeb";
        return (firstName, lastName);
    }

    private static SubscriptionPlan ToPlan(BillingProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static ShopperSubscription ToShopperSubscription(BillingSubscription subscription, BillingProduct? product) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = product?.Handle ?? subscription.Product?.Handle ?? string.Empty,
        ProductName = product?.Name ?? subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents != 0 ? subscription.ProductPriceInCents : product?.PriceInCents ?? 0,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        Reference = subscription.Reference
    };
}
