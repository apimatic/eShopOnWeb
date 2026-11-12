using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maps an eShop user to its Maxio customer. The primary key (UserId) is the claim that
/// stops two concurrent requests from both creating a Maxio customer for the same user.
/// </summary>
public class MaxioCustomerLink : IAggregateRoot
{
    public string UserId { get; private set; }
    public int? MaxioCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private MaxioCustomerLink() { }
#pragma warning restore CS8618

    public MaxioCustomerLink(string userId)
    {
        UserId = userId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void SetMaxioCustomerId(int id) => MaxioCustomerId = id;
}
