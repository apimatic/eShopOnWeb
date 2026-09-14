using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public class MaxioCustomerLink : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    protected MaxioCustomerLink()
    {
    }

    public MaxioCustomerLink(string userId, string customerReference, int maxioCustomerId, DateTimeOffset createdAt)
    {
        UserId = Require(userId, nameof(userId));
        CustomerReference = Require(customerReference, nameof(customerReference));
        MaxioCustomerId = maxioCustomerId;
        CreatedAt = createdAt;
    }

    private static string Require(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

        return value;
    }
}
