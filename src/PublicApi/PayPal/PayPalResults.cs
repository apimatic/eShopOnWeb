namespace Microsoft.eShopWeb.PublicApi.PayPal;

public record AuthorizeResult(string PayPalOrderId, string AuthorizationId);

public record CaptureResult(string CaptureId, decimal CapturedAmount, decimal PayPalFee, decimal NetAmount);

public record RefundResult(string RefundId);

public record VaultCardResult(string VaultToken, string? PayPalCustomerId, string? CardBrand, string? Last4, string? Expiry);

public record TransactionItem(
    string? TransactionId,
    string? Amount,
    string? Currency,
    string? Fee,
    string? Status,
    string? InitiationDate,
    string? EventCode);
