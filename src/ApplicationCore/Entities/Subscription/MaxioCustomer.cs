using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;

public class MaxioCustomer : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = null!;
    public long MaxioCustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
