using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedCard : BaseEntity
{
    public string PublicId { get; set; } = Guid.NewGuid().ToString();
    public string BuyerId { get; set; } = string.Empty;
    public string FinixPaymentInstrumentId { get; set; } = string.Empty;
    public string FinixIdentityId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
