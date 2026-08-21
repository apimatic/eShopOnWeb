namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalOrderIdentifiers
{
    public static string InvoiceId(int orderId) => $"ESHOP-{orderId}";
    public static string CustomId(int orderId) => $"order-{orderId}";
}
