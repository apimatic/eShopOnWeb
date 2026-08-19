using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Spec collection method for Relationship Invoicing when no card is captured.
    /// Seeded catalog products do not require a payment method.
    /// </summary>
    public const string RemittanceCollectionMethod = "remittance";

    private readonly IAdvancedBillingGateway _gateway;
    private readonly UserKeyedLock _userKeyedLock;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IAdvancedBillingGateway gateway,
        UserKeyedLock userKeyedLock,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _gateway = gateway;
        _userKeyedLock = userKeyedLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _gateway.ListCatalogPlansAsync(cancellationToken);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public Task<SubscribeResult> SubscribeAsync(BillingShopper shopper, string productHandle, CancellationToken cancellationToken)
    {
        if (shopper is null)
        {
            throw new ArgumentNullException(nameof(shopper));
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingValidationException("A productHandle is required.");
        }

        var handle = productHandle.Trim();
        return _userKeyedLock.RunAsync(shopper.UserId, () => SubscribeCoreAsync(shopper, handle, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(BillingShopper shopper, CancellationToken cancellationToken)
    {
        if (shopper is null)
        {
            throw new ArgumentNullException(nameof(shopper));
        }

        var customer = await _gateway.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<UserSubscription>();
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToUserSubscription).ToList();
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(BillingShopper shopper, string productHandle, CancellationToken cancellationToken)
    {
        var product = await _gateway.ReadProductByHandleAsync(productHandle, cancellationToken);
        if (product is null)
        {
            throw new BillingValidationException($"Unknown subscription plan '{productHandle}'.");
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var stableReference = SubscriptionReference.ForPlan(shopper.UserId, productHandle);

        var existingByReference = await _gateway.FindSubscriptionByReferenceAsync(stableReference, cancellationToken);
        if (existingByReference is not null && BillingState.IsExistingEnrollment(existingByReference.State))
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.", existingByReference.Id, shopper.UserId, productHandle);
            return new SubscribeResult(ToUserSubscription(existingByReference), created: false);
        }

        var customerSubscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existingForPlan = customerSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
            && BillingState.IsExistingEnrollment(s.State));
        if (existingForPlan is not null)
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.", existingForPlan.Id, shopper.UserId, productHandle);
            return new SubscribeResult(ToUserSubscription(existingForPlan), created: false);
        }

        var reference = existingByReference is null
            ? stableReference
            : SubscriptionReference.ForReenrollment(shopper.UserId, productHandle);

        try
        {
            var created = await _gateway.CreateSubscriptionAsync(
                new CreateBillingSubscription(productHandle, customer.Id, reference, RemittanceCollectionMethod),
                cancellationToken);
            return new SubscribeResult(ToUserSubscription(created), created: true);
        }
        catch (BillingGatewayException ex) when (ex.StatusCode == 422)
        {
            var raced = await _gateway.FindSubscriptionByReferenceAsync(reference, cancellationToken)
                ?? (await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                    .FirstOrDefault(s =>
                        string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
                        && BillingState.IsExistingEnrollment(s.State));

            if (raced is not null)
            {
                _logger.LogWarning("Subscribe raced for user {UserId} plan {Plan}; returning existing subscription {SubscriptionId}.", shopper.UserId, productHandle, raced.Id);
                return new SubscribeResult(ToUserSubscription(raced), created: false);
            }

            throw;
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(BillingShopper shopper, CancellationToken cancellationToken)
    {
        var existing = await _gateway.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ShopperName.FromEmail(shopper.Email, shopper.UserName);
        var create = new CreateBillingCustomer(firstName, lastName, shopper.Email, shopper.UserId);

        try
        {
            return await _gateway.CreateCustomerAsync(create, cancellationToken);
        }
        catch (BillingGatewayException ex) when (ex.StatusCode == 422)
        {
            var raced = await _gateway.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                _logger.LogWarning("Create customer raced for user {UserId}; returning existing Maxio customer {CustomerId}.", shopper.UserId, raced.Id);
                return raced;
            }

            throw;
        }
    }

    private static SubscriptionPlan ToPlan(BillingProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        Price = Money.FromCents(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequireCreditCard = product.RequireCreditCard
    };

    private static UserSubscription ToUserSubscription(BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        Price = Money.FromCents(subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0),
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        Reference = subscription.Reference
    };
}
