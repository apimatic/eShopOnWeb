using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps an eShop shopper to the Finix buyer <c>Identity</c> that owns their payment
/// instruments. One identity per shopper, created on first use.
/// </summary>
public class FinixBuyerProfile : IAggregateRoot
{
    public string BuyerId { get; set; } = string.Empty;
    public string FinixIdentityId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
