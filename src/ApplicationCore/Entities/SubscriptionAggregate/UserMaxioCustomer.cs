using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserMaxioCustomer : BaseEntity
{
    public string ApplicationUserId { get; set; } = null!;
    public long MaxioCustomerId { get; set; }
    public string MaxioCustomerReference { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
