namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal FeeAmount,
    decimal NetAmount,
    string CurrencyCode);
