namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The invoice id sent to PayPal when the order is created there. Defined once so the gateway
/// that stamps it and the reconciliation that matches on it can never drift apart. The key
/// suffix makes it unique per payment attempt, not just per order.
/// </summary>
public static class PaymentInvoiceId
{
    public static string For(int orderId, string createRequestKey) => $"order-{orderId}-{createRequestKey[^8..]}";
}
