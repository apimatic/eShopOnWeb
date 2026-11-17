using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A Finix payment instrument saved by a shopper. Only non-sensitive descriptor fields are kept.
/// </summary>
public class SavedCard
{
    public int Id { get; set; }
    public string ShopperId { get; set; } = string.Empty;
    public string PaymentInstrumentId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
