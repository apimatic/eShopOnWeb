using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class Subscription : BaseEntity, IAggregateRoot
{
    public required string UserId { get; set; }
    public required int MaxioSubscriptionId { get; set; }
    public required int MaxioProductId { get; set; }
    public required string ProductHandle { get; set; }
    public required string State { get; set; }
    public decimal CurrentPrice { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
