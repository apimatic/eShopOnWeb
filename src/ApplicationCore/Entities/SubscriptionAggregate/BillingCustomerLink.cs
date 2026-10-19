using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShop identity user to their customer record in the billing provider.
/// The row doubles as the claim that stops two requests creating two provider customers:
/// its primary key is the user id, so a second insert for the same user is refused by the store.
/// </summary>
public class BillingCustomerLink : IAggregateRoot
{
    public string UserId { get; private set; }
    public ClaimStatus Status { get; private set; }
    public int? ProviderCustomerId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }

    /// <summary>Concurrency token: changes every time the claim is (re)taken.</summary>
    public Guid ClaimToken { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomerLink() { }

    public BillingCustomerLink(string userId, DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        UserId = userId;
        Status = ClaimStatus.Pending;
        ClaimedAt = claimedAt;
        ClaimToken = Guid.NewGuid();
    }

    public bool IsStale(DateTimeOffset now, TimeSpan maxAge) =>
        Status == ClaimStatus.Pending && now - ClaimedAt > maxAge;

    public void Retake(DateTimeOffset now)
    {
        ClaimedAt = now;
        ClaimToken = Guid.NewGuid();
    }

    public void Complete(int providerCustomerId)
    {
        ProviderCustomerId = providerCustomerId;
        Status = ClaimStatus.Completed;
    }
}
