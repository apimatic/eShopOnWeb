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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var products = await ListFamilyProductsAsync(cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        if (shopper is null)
        {
            throw new BillingValidationException("A shopper identity is required to subscribe.");
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingValidationException("productHandle is required.");
        }

        productHandle = productHandle.Trim();
        await EnsurePlanIsOfferedAsync(productHandle, cancellationToken);

        var lockKey = $"{shopper.UserId}:{productHandle}";
        var gate = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var reference = CanonicalSubscriptionReference(shopper.UserId, productHandle);

            var existing = await FindLiveSubscriptionAsync(customer.Id!.Value, productHandle, reference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Idempotent subscribe: shopper {UserId} already has subscription {SubscriptionId} for {Handle}.",
                    shopper.UserId, existing.Id, productHandle);
                return new SubscribeResult(ToShopperSubscription(existing), Created: false);
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
                {
                    Subscription = new MaxioCreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customer.Id,
                        Reference = reference,
                        // Spec createSubscription "Basic" example: remittance enrolls without a card on file.
                        PaymentCollectionMethod = "remittance"
                    }
                }, cancellationToken);

                return new SubscribeResult(ToShopperSubscription(created), Created: true);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // Lost the create race: another request created the same reference.
                var raced = await FindLiveSubscriptionAsync(customer.Id!.Value, productHandle, reference, cancellationToken);
                if (raced is not null)
                {
                    return new SubscribeResult(ToShopperSubscription(raced), Created: false);
                }

                throw new BillingValidationException(
                    string.IsNullOrWhiteSpace(ex.Message)
                        ? "Maxio rejected the subscription."
                        : ex.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        if (shopper is null)
        {
            throw new BillingValidationException("A shopper identity is required.");
        }

        var customer = await _maxio.ReadCustomerByReferenceAsync(CustomerReference(shopper.UserId), cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(ToShopperSubscription).ToList();
    }

    public static string CustomerReference(string userId) => userId;

    public static string CanonicalSubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    public static bool IsTerminal(string? state) =>
        state is "canceled" or "expired" or "failed_to_create" or "trial_ended";

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(shopper.UserId);
        var existing = await _maxio.ReadCustomerByReferenceAsync(reference, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                Customer = new MaxioCreateCustomer
                {
                    FirstName = shopper.FirstName,
                    LastName = shopper.LastName,
                    Email = shopper.Email,
                    Reference = reference
                }
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.ReadCustomerByReferenceAsync(reference, cancellationToken);
            if (raced?.Id is not null)
            {
                return raced;
            }

            throw new BillingValidationException(
                string.IsNullOrWhiteSpace(ex.Message)
                    ? "Maxio rejected the customer."
                    : ex.Message);
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (byReference is not null && !IsTerminal(byReference.State))
        {
            return byReference;
        }

        var listed = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return listed.FirstOrDefault(s =>
            !IsTerminal(s.State)
            && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task EnsurePlanIsOfferedAsync(string productHandle, CancellationToken cancellationToken)
    {
        var products = await ListFamilyProductsAsync(cancellationToken);
        var match = products.FirstOrDefault(p =>
            p.ArchivedAt is null
            && string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new BillingValidationException(
                $"Plan '{productHandle}' is not an available subscription plan.");
        }
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        // Spec: product_family_id is "Either the product family's id or its handle prefixed with `handle:`"
        var familyKey = $"handle:{_options.ProductFamilyHandle}";
        const int perPage = 200;
        var page = 1;
        var all = new List<MaxioProduct>();

        while (true)
        {
            var batch = await _maxio.ListProductsForProductFamilyAsync(
                familyKey, page, perPage, includeArchived: false, cancellationToken);
            all.AddRange(batch);
            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return all;
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product)
    {
        return new SubscriptionPlan(
            product.Id ?? 0,
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.Description,
            CentsToPrice(product.PriceInCents),
            product.Interval ?? 0,
            product.IntervalUnit ?? string.Empty,
            product.RequireCreditCard ?? false);
    }

    private static ShopperSubscription ToShopperSubscription(MaxioSubscription subscription)
    {
        var priceCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents;
        return new ShopperSubscription(
            subscription.Id ?? 0,
            subscription.Reference,
            subscription.State ?? string.Empty,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            CentsToPrice(priceCents),
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);
    }

    private static decimal CentsToPrice(long? cents) =>
        cents is null ? 0m : cents.Value / 100m;
}
