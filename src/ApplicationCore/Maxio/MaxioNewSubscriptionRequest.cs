namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioNewSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? Reference { get; set; }

    /// <summary>
    /// How Maxio collects payment. "remittance" issues an invoice and does not require a
    /// stored payment method — matching the seeded plans' "payment method not required" setup.
    /// Values come from the spec's Collection-Method schema: automatic | remittance | prepaid | invoice.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = MaxioCollectionMethods.Remittance;
}

