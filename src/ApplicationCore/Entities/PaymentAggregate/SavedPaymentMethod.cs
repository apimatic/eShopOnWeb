using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedPaymentMethod : IAggregateRoot
{
    public int Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string BuyerId { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string FinixPaymentInstrumentId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? Brand { get; set; }

    [MaxLength(4)]
    public string? LastFour { get; set; }

    public int? ExpirationMonth { get; set; }

    public int? ExpirationYear { get; set; }

    [MaxLength(128)]
    public string? Fingerprint { get; set; }

    [MaxLength(128)]
    public string? Alias { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DisabledAt { get; set; }
}
