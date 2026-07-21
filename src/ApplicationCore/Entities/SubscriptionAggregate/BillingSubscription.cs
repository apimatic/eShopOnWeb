using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription as it exists with the billing provider. eShopOnWeb keeps no persisted copy of this
/// - Maxio is the system of record and this is always a live read.
/// </summary>
public class BillingSubscription
{
    public BillingSubscription(
        int id,
        BillingSubscriptionState state,
        int customerId,
        string customerReference,
        int productId,
        string productHandle,
        string productName,
        long priceInCents,
        long balanceInCents,
        DateTimeOffset? currentPeriodEndsAt,
        string? nextProductHandle,
        bool cancelAtEndOfPeriod)
    {
        Guard.Against.NegativeOrZero(id, nameof(id));
        Guard.Against.NegativeOrZero(customerId, nameof(customerId));
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        Guard.Against.NegativeOrZero(productId, nameof(productId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));
        Guard.Against.NullOrEmpty(productName, nameof(productName));

        Id = id;
        State = state;
        CustomerId = customerId;
        CustomerReference = customerReference;
        ProductId = productId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        BalanceInCents = balanceInCents;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextProductHandle = nextProductHandle;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
    }

    public int Id { get; }
    public BillingSubscriptionState State { get; }
    public int CustomerId { get; }
    public string CustomerReference { get; }
    public int ProductId { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public long PriceInCents { get; }
    public long BalanceInCents { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>Set when a delayed (at-renewal) plan change is pending.</summary>
    public string? NextProductHandle { get; }

    /// <summary>Set when a delayed (end-of-period) cancellation is pending.</summary>
    public bool CancelAtEndOfPeriod { get; }

    private static readonly IReadOnlyCollection<BillingSubscriptionState> LiveStates = new[]
    {
        BillingSubscriptionState.Active,
        BillingSubscriptionState.Trialing,
        BillingSubscriptionState.PastDue,
        BillingSubscriptionState.Unpaid,
        BillingSubscriptionState.Suspended,
        BillingSubscriptionState.Paused,
        BillingSubscriptionState.Assessing,
        BillingSubscriptionState.SoftFailure,
    };

    /// <summary>Whether this subscription counts as an existing enrollment for double-enrollment checks.</summary>
    public bool IsLive => LiveStates.Contains(State);
}
