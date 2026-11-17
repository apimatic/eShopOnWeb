using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BuyerId { get; set; } = string.Empty;
    public string CustomerIdentityId { get; set; } = string.Empty;
    public string FinixPaymentInstrumentId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
