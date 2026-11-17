using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class FinixBuyerIdentity : IAggregateRoot
{
    [Key]
    [MaxLength(256)]
    public string BuyerId { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string FinixIdentityId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
