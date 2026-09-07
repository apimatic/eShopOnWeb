using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class MaxioCustomer : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public MaxioCustomer(string userId, int maxioCustomerId, DateTime createdAt, DateTime updatedAt)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(maxioCustomerId, nameof(maxioCustomerId));

        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }
}
