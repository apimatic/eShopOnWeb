using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class MaxioSubscriptionMapping : BaseEntity, IAggregateRoot
{
    public string? ApplicationUserId { get; set; }
    public int MaxioCustomerId { get; set; }
}
