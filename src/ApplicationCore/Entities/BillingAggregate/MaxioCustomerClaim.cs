using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Claim on "create the billing-provider customer for user X". The primary key (UserId) refuses a second
/// concurrent customer creation for the same user.
/// </summary>
public class MaxioCustomerClaim : IAggregateRoot
{
    public string UserId { get; private set; }
    public int? ProviderCustomerId { get; private set; }
    public DateTime ClaimedAtUtc { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private MaxioCustomerClaim() { }
#pragma warning restore CS8618

    public MaxioCustomerClaim(string userId, DateTime claimedAtUtc)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        UserId = userId;
        ClaimedAtUtc = claimedAtUtc;
    }

    public void Complete(int providerCustomerId) => ProviderCustomerId = providerCustomerId;
}
