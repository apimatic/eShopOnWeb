namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    private PaymentMethod() { }

    public PaymentMethod(string alias, string operationKey)
    {
        Alias = alias;
        OperationKey = operationKey;
    }

    public string? Alias { get; private set; }
    public string? CardId { get; private set; } // PayPal vault id; PAN/CVC are never persisted.
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string OperationKey { get; private set; } = string.Empty;
    public PaymentMethodStatus Status { get; private set; } = PaymentMethodStatus.Pending;

    public void Activate(string cardId, string last4, string? brand, string? expiry, string? payPalCustomerId)
    {
        CardId = cardId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        PayPalCustomerId = payPalCustomerId;
        Status = PaymentMethodStatus.Active;
    }

    public void BeginDelete() => Status = PaymentMethodStatus.DeletePending;
}

public enum PaymentMethodStatus
{
    Pending,
    Active,
    DeletePending
}
