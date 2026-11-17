using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A short-lived claim that stops a double-click from saving the same card twice. The key is
/// derived from the shopper and the non-sensitive card descriptor; the PAN is never stored.
/// </summary>
public class SavedCardClaim
{
    public string ClaimKey { get; set; } = string.Empty;
    public string? PaymentInstrumentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
