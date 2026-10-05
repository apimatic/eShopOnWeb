using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShopOnWeb user to their customer record in the billing system.
/// The row doubles as the claim that stops two concurrent requests from creating two billing customers:
/// it is inserted (primary key = <see cref="UserId"/>) before the billing customer is created.
/// </summary>
public class BillingCustomer
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomer() { }
    #pragma warning restore CS8618

    public BillingCustomer(string userId, DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        UserId = userId;
        BillingReference = ReferenceFor(userId);
        ClaimedAt = claimedAt;
        ClaimToken = Guid.NewGuid();
    }

    public string UserId { get; private set; }

    /// <summary>The unique customer reference sent to the billing system; derived from the user id.</summary>
    public string BillingReference { get; private set; }

    /// <summary>The billing system's customer id; null while the claim is still pending.</summary>
    public int? BillingCustomerId { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }

    /// <summary>Concurrency token: changes whenever a request takes over a stale claim.</summary>
    public Guid ClaimToken { get; private set; }

    public bool IsLinked => BillingCustomerId.HasValue;

    public static string ReferenceFor(string userId) => $"eshop-user-{userId}";

    public void Link(int billingCustomerId)
    {
        Guard.Against.NegativeOrZero(billingCustomerId, nameof(billingCustomerId));
        BillingCustomerId = billingCustomerId;
    }

    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter) => !IsLinked && now - ClaimedAt > staleAfter;

    public void Renew(DateTimeOffset now)
    {
        ClaimedAt = now;
        ClaimToken = Guid.NewGuid();
    }
}
