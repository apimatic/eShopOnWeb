namespace Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

public record PayPalCardRequest(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    string? BillingAddressLine1 = null,
    string? BillingCity = null,
    string? BillingState = null,
    string? BillingPostalCode = null,
    string? BillingCountryCode = null);

public record PayPalAuthorizeResult(
    string AuthorizationId,
    string Status,
    string? PayPalOrderId);

public record PayPalAuthorizationDetails(
    string AuthorizationId,
    string Status,
    string? CreateTime,
    string? ExpirationTime);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount);

public record PayPalVaultResult(
    string PaymentTokenId,
    string? Last4,
    string? Brand,
    string? Expiry,
    string? CardholderName);

public record PayPalTransactionRecord(
    string? TransactionId,
    string? PayPalReferenceId,
    string? Status,
    decimal? Amount,
    string? InitiationDate,
    string? InvoiceId);
