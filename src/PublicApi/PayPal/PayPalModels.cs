namespace Microsoft.eShopWeb.PublicApi.PayPal;

public record CardPaymentDetails(
    string Number,
    string ExpiryYear,
    string ExpiryMonth,
    string Cvv,
    string CardholderName,
    string Street,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    string? ExpirationTime);

public record CaptureResult(
    string CaptureId,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CaptureStatus);

public record ReauthorizeResult(
    string NewAuthorizationId,
    string NewStatus,
    string? NewExpirationTime);

public record RefundResult(
    string RefundId,
    decimal RefundedAmount,
    string RefundStatus);

public record VaultResult(
    string VaultToken,
    string? Last4Digits,
    string? CardBrand,
    string? Expiry);

public record VaultedPaymentMethodInfo(
    string VaultToken,
    string? Last4Digits,
    string? CardBrand,
    string? Expiry);

public record PayPalTransactionInfo(
    string TransactionId,
    decimal? Amount,
    decimal? Fee,
    string? Status,
    string? InvoiceId,
    string? CustomField);
