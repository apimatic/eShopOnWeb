using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class MaxioCustomerMapping : BaseEntity, IAggregateRoot
{
    private MaxioCustomerMapping() { }

    public MaxioCustomerMapping(string applicationUserId, int maxioCustomerId, string maxioReference)
    {
        ApplicationUserId = applicationUserId;
        MaxioCustomerId = maxioCustomerId;
        MaxioReference = maxioReference;
        CreatedUtc = DateTimeOffset.UtcNow;
    }

    public string ApplicationUserId { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public string MaxioReference { get; private set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; private set; }
}
