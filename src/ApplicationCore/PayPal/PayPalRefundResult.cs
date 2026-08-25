namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);
