using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class MaxioCustomer : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
