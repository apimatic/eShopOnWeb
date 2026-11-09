using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

/// <summary>
/// Remembers which Stax payment methods were saved through the shop. Only the Stax id is kept: card and
/// bank details live at Stax. Lets support find methods even after they were removed.
/// </summary>
public class SavedPaymentMethod : BaseEntity
{
    public string ShopperEmail { get; private set; }
    public string StaxPaymentMethodId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string shopperEmail, string staxPaymentMethodId)
    {
        ShopperEmail = shopperEmail;
        StaxPaymentMethodId = staxPaymentMethodId;
    }

    public void MarkRemoved() => RemovedAt ??= DateTimeOffset.UtcNow;
}
