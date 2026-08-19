using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.MaxioModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string CustomerReferencePrefix = "eshoponweb:";

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
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        var handle = productHandle.Trim();
        var plans = await ListAvailablePlansAsync(cancellationToken);
        if (plans.All(p => !string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PlanNotFoundException(handle);
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var subscriptionReference = BuildSubscriptionReference(shopper.Email, handle);

        var existing = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for reference {Reference}",
                existing.Id, subscriptionReference);
            return ToCustomerSubscription(existing);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(new CreateSubscriptionBody
            {
                ProductHandle = handle,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            return ToCustomerSubscription(created);
        }
        catch (BillingGatewayException ex) when (ex.StatusCode is 409 or 422)
        {
            // Concurrent double-click: the unique subscription reference already exists.
            var raced = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (raced is not null)
            {
                return ToCustomerSubscription(raced);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForShopperAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);

        var customer = await _maxio.FindCustomerByReferenceAsync(BuildCustomerReference(shopper.Email), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToCustomerSubscription).ToList();
    }

    private async Task<CustomerDto> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(shopper.Email);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(new CreateCustomerBody
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = reference
            }, cancellationToken);
        }
        catch (BillingGatewayException ex) when (ex.StatusCode is 409 or 422)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    public static string BuildCustomerReference(string email) =>
        CustomerReferencePrefix + email.Trim().ToLowerInvariant();

    public static string BuildSubscriptionReference(string email, string productHandle) =>
        $"{BuildCustomerReference(email)}:{productHandle.Trim().ToLowerInvariant()}";

    private static SubscriptionPlan ToPlan(ProductDto product) =>
        new(
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit ?? "month");

    private static CustomerSubscription ToCustomerSubscription(SubscriptionDto subscription)
    {
        var productHandle = subscription.Product?.Handle ?? string.Empty;
        var productName = subscription.Product?.Name ?? productHandle;
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new CustomerSubscription(
            subscription.Id,
            productHandle,
            productName,
            priceInCents,
            subscription.State ?? "unknown",
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt);
    }
}
