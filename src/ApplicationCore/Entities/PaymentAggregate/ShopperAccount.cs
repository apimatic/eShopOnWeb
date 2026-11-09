using System;

using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Links a shop account (by e-mail) to its customer record at Stax.
/// </summary>
public class ShopperAccount : BaseEntity, IAggregateRoot
{
    public string Email { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
