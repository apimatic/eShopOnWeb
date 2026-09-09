using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Enrollment of eShopOnWeb buyers into recurring subscription plans.
/// The external billing system (Maxio Advanced Billing) is the system of record;
/// all operations are idempotent so repeated calls never create duplicate
/// customers or subscriptions.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Prefix for the reference stored on the billing-system customer so the
    /// eShopOnWeb buyer can always be mapped back idempotently. Keyed on the
    /// buyer's (normalized) email, which is stable across re-seeds of the
    /// identity store, unlike the auto-generated user id.
    /// </summary>
    public const string CustomerReferencePrefix = "eshopweb:";

    /// <summary>
    /// Subscription states that mean "this buyer already holds this plan";
    /// canceled/expired subscriptions do not block re-subscribing.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "awaiting_signup", "soft_failure",
        "past_due", "unpaid", "suspended", "on_hold", "trial_ended"
    };

    private readonly IMaxioBillingClient _billingClient;
    private readonly IRepository<SubscriptionRecord> _subscriptionRepository;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioBillingClient billingClient,
        IRepository<SubscriptionRecord> subscriptionRepository,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return await _billingClient.ListPlansAsync(cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(BuyerIdentity buyer, string planHandle,
        CancellationToken cancellationToken = default)
    {
        var plans = await _billingClient.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new PlanNotFoundException(planHandle);
        }

        var customer = await EnsureCustomerAsync(buyer, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Buyer {BuyerId} already holds subscription {SubscriptionId} for plan {PlanHandle}; returning it.",
                buyer.BuyerId, existing.Id, plan.Handle);
            var existingRecord = await UpsertRecordAsync(buyer.BuyerId, customer.Id, existing, cancellationToken);
            return new SubscribeResult(ToView(existing, existingRecord), CreatedNew: false);
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        var record = await UpsertRecordAsync(buyer.BuyerId, customer.Id, subscription, cancellationToken);

        _logger.LogInformation("Buyer {BuyerId} subscribed to plan {PlanHandle}; billing subscription {SubscriptionId} in state {State}.",
            buyer.BuyerId, plan.Handle, subscription.Id, subscription.State);

        return new SubscribeResult(ToView(subscription, record), CreatedNew: true);
    }

    public async Task<IReadOnlyList<SubscriptionView>> ListForBuyerAsync(BuyerIdentity buyer,
        CancellationToken cancellationToken = default)
    {
        var reference = CustomerReference(buyer);
        var customer = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionView>();
        }

        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        var records = (await _subscriptionRepository.ListAsync(new BuyerSubscriptionsSpecification(buyer.BuyerId), cancellationToken))
            .ToDictionary(r => r.BillingSubscriptionId);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(s =>
            {
                records.TryGetValue(s.Id, out var record);
                return ToView(s, record);
            })
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(BuyerIdentity buyer, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(buyer);
        var customer = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        customer = await _billingClient.CreateCustomerAsync(reference, buyer.Email, buyer.FirstName, buyer.LastName, cancellationToken);
        _logger.LogInformation("Created billing customer {CustomerId} (reference {Reference}) for buyer {BuyerId}.",
            customer.Id, reference, buyer.BuyerId);
        return customer;
    }

    private async Task<MaxioSubscriptionInfo?> FindLiveSubscriptionAsync(int billingCustomerId, string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(billingCustomerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase)
            && LiveStates.Contains(s.State));
    }

    private async Task<SubscriptionRecord> UpsertRecordAsync(string buyerId, int billingCustomerId,
        MaxioSubscriptionInfo subscription, CancellationToken cancellationToken)
    {
        var spec = new BuyerSubscriptionsSpecification(buyerId, subscription.PlanHandle);
        var record = await _subscriptionRepository.FirstOrDefaultAsync(spec, cancellationToken);
        if (record is null)
        {
            record = new SubscriptionRecord(buyerId, billingCustomerId, subscription.Id,
                subscription.PlanHandle, subscription.PlanName ?? subscription.PlanHandle,
                subscription.PriceInCents, subscription.State);
            record = await _subscriptionRepository.AddAsync(record, cancellationToken);
        }
        else
        {
            record.UpdateState(subscription.State, subscription.PriceInCents);
            await _subscriptionRepository.UpdateAsync(record, cancellationToken);
        }

        return record;
    }

    private static SubscriptionView ToView(MaxioSubscriptionInfo subscription, SubscriptionRecord? record)
    {
        return new SubscriptionView(
            subscription.Id,
            record?.BillingCustomerId ?? 0,
            subscription.PlanHandle,
            record?.PlanName ?? subscription.PlanName ?? subscription.PlanHandle,
            record?.PriceInCents ?? subscription.PriceInCents,
            subscription.State,
            subscription.CurrentPeriodStartsAt,
            subscription.CurrentPeriodEndsAt,
            subscription.NextBillingAt,
            subscription.CreatedAt);
    }

    private static string CustomerReference(BuyerIdentity buyer) =>
        $"{CustomerReferencePrefix}{buyer.Email.Trim().ToLowerInvariant()}";
}
