using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates subscription billing with Maxio Advanced Billing as the system of record.
/// Maxio customers are keyed by the eShopOnWeb identity user id (stored as the customer
/// "reference"), so a repeat subscribe never creates a second customer or a duplicate
/// subscription for the same plan.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshop-user-";
    private const string PaymentCollectionMethod = "remittance";

    private static readonly string[] LiveSubscriptionStates =
    {
        "active", "trialing", "awaiting_signup", "on_hold", "past_due", "unpaid"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly MaxioApiClient _maxio;
    private readonly MaxioOptions _options;

    public SubscriptionService(MaxioApiClient maxio, IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _options = options.Value;
        _options.Validate();
    }

    public async Task<IReadOnlyList<SubscriptionPlanSummary>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlanSummary
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = ToPrice(p.PriceInCents),
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                Taxable = p.Taxable
            })
            .ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(SubscriptionUserContext user, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionPlanNotFoundException(productHandle ?? string.Empty);
        }

        var product = await _maxio.GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null || product.ArchivedAt is not null || product.ProductFamily?.Handle != _options.ProductFamilyHandle)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);

        // Serialize per-user so a double-click cannot create two subscriptions.
        var userLock = UserLocks.GetOrAdd(user.UserId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var subscriptions = await _maxio.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(s =>
                s.Product?.Handle == productHandle && LiveSubscriptionStates.Contains(s.State));
            if (existing is not null)
            {
                return Map(existing);
            }

            var created = await _maxio.CreateSubscriptionAsync(customer.Id, productHandle, PaymentCollectionMethod, cancellationToken);
            return Map(created);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(SubscriptionUserContext user, CancellationToken cancellationToken = default)
    {
        var reference = CustomerReference(user.UserId);
        var customer = await _maxio.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _maxio.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionUserContext user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.UserId);
        var existing = await _maxio.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(
                reference,
                user.Email,
                firstName: "eShop",
                lastName: "Customer",
                organization: user.UserName,
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Another concurrent request may have created the customer between the lookup
            // and the create; fall back to the lookup so we never surface a duplicate error.
            var raced = await _maxio.LookupCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }
    }

    private static string CustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";

    private static SubscriptionSummary Map(MaxioSubscription subscription)
    {
        return new SubscriptionSummary
        {
            SubscriptionId = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            Price = ToPrice(subscription.ProductPriceInCents),
            Currency = subscription.Currency,
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt
        };
    }

    private static decimal ToPrice(int cents) => cents / 100m;
}