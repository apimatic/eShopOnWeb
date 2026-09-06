using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class MaxioCustomer : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = null!;
    public int MaxioCustomerId { get; set; }
    public string MaxioReference { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
