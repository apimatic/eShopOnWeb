using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShopOnWeb identity to its customer record in the billing system.
/// The row doubles as the claim that serialises customer creation: its primary key is the buyer id,
/// so a second concurrent insert for the same buyer is refused by the store.
/// </summary>
public class BillingCustomer : IAggregateRoot
{
    public const string ReferencePrefix = "eshop-user-";

    public string BuyerId { get; private set; }
    public string CustomerReference { get; private set; }
    public int? BillingCustomerId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomer() { }

    public BillingCustomer(string buyerId, DateTimeOffset claimedAt) : this()
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        BuyerId = buyerId;
        CustomerReference = ReferenceFor(buyerId);
        ClaimedAt = claimedAt;
    }

    /// <summary>The stable reference the billing system stores for this buyer (unique per billing site).</summary>
    public static string ReferenceFor(string buyerId) => ReferencePrefix + buyerId;

    public void RecordBillingCustomer(int billingCustomerId)
    {
        Guard.Against.NegativeOrZero(billingCustomerId, nameof(billingCustomerId));
        BillingCustomerId = billingCustomerId;
    }

    /// <summary>True when the claim is old enough that no request can still be working on it.</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter) => now - ClaimedAt > staleAfter;
}
