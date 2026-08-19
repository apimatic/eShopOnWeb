using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new(StringComparer.Ordinal);
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

    private readonly IMaxioAdvancedBillingClient _client;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _options.Value.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set it from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }

        var products = await _client.ListProductsForFamilyAsync(familyHandle, cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        var gate = SubscribeLocks.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(shopper, productHandle.Trim(), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<ShopperSubscription>();
        }

        var customer = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<ShopperSubscription> SubscribeCoreAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var product = await _client.GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null || product.ArchivedAt is not null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Returning existing Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.",
                existing.Id,
                shopper.UserId,
                productHandle);
            return MapSubscription(existing);
        }

        var reference = BuildSubscriptionReference(shopper.UserId, productHandle);
        var byReference = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (byReference is not null && IsLive(byReference.State))
        {
            return MapSubscription(byReference);
        }

        try
        {
            var created = await _client.CreateSubscriptionAsync(new MaxioCreateSubscriptionPayload
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.",
                created.Id,
                shopper.UserId,
                productHandle);

            return MapSubscription(created);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            var raced = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken)
                        ?? await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return MapSubscription(raced);
            }

            throw;
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.CreateCustomerAsync(new MaxioCreateCustomerPayload
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            existing = await _client.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription.State) &&
            string.Equals(ResolvePlanHandle(subscription), productHandle, StringComparison.OrdinalIgnoreCase));
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle)
        => $"eshop:{userId}:{productHandle}";

    private static bool IsLive(string? state)
        => !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);

    private static string ResolvePlanHandle(MaxioSubscription subscription)
        => subscription.Product?.Handle ?? subscription.ProductHandle ?? string.Empty;

    private static SubscriptionPlan MapPlan(MaxioProduct product)
        => new(
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            CentsToDecimal(product.PriceInCents),
            product.Interval,
            product.IntervalUnit ?? "month");

    private static ShopperSubscription MapSubscription(MaxioSubscription subscription)
    {
        var handle = ResolvePlanHandle(subscription);
        var name = subscription.Product?.Name ?? handle;
        var priceCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new ShopperSubscription(
            subscription.Id,
            handle,
            name,
            CentsToDecimal(priceCents),
            subscription.State ?? "unknown",
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static decimal CentsToDecimal(long cents) => cents / 100m;
}
