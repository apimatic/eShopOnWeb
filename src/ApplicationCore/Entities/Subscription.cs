using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string PlanHandle { get; set; }
    public string State { get; set; }
    public decimal PriceInCents { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
