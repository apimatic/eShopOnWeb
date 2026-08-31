using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

// DTOs mirroring the PayPal OpenAPI schemas (api-specs/paypal). Property names are
// serialized snake_case via JsonNamingPolicy.SnakeCaseLower configured in PayPalClient.

public class PayPalMoney
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class PayPalAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

public class PayPalCardRequest
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayPalAddress? BillingAddress { get; set; }
    public string? VaultId { get; set; }
}

public class PayPalCardResponse
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

public class PayPalPaymentSourceRequest
{
    public PayPalCardRequest? Card { get; set; }
}

public class PayPalPurchaseUnitRequest
{
    public PayPalMoney Amount { get; set; } = new();
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
}

public class PayPalOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

public class PayPalSellerReceivableBreakdown
{
    public PayPalMoney? GrossAmount { get; set; }
    public PayPalMoney? PaypalFee { get; set; }
    public PayPalMoney? NetAmount { get; set; }
}

public class PayPalSellerPayableBreakdown
{
    public PayPalMoney? GrossAmount { get; set; }
    public PayPalMoney? PaypalFee { get; set; }
    public PayPalMoney? NetAmount { get; set; }
    public PayPalMoney? TotalRefundedAmount { get; set; }
}

public class PayPalAuthorization
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
    public DateTimeOffset? ExpirationTime { get; set; }
    public List<PayPalLinkDescription>? Links { get; set; }
}

public class PayPalCapture
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
    public bool? FinalCapture { get; set; }
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

public class PayPalRefund
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
    public PayPalSellerPayableBreakdown? SellerPayableBreakdown { get; set; }
}

public class PayPalPaymentCollection
{
    public List<PayPalAuthorization>? Authorizations { get; set; }
    public List<PayPalCapture>? Captures { get; set; }
    public List<PayPalRefund>? Refunds { get; set; }
}

public class PayPalPurchaseUnit
{
    public PayPalPaymentCollection? Payments { get; set; }
}

public class PayPalOrderResponse
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
    public List<PayPalLinkDescription>? Links { get; set; }
}

public class PayPalLinkDescription
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

public class PayPalCaptureRequest
{
    public PayPalMoney? Amount { get; set; }
    public string? InvoiceId { get; set; }
    public bool FinalCapture { get; set; } = true;
}

public class PayPalReauthorizeRequest
{
    public PayPalMoney? Amount { get; set; }
}

public class PayPalRefundRequest
{
    public PayPalMoney? Amount { get; set; }
    public string? CustomId { get; set; }
    public string? NoteToPayer { get; set; }
}

public class PayPalVaultTokenRequest
{
    public PayPalVaultPaymentSourceRequest PaymentSource { get; set; } = new();
}

public class PayPalVaultPaymentSourceRequest
{
    public PayPalCardRequest Card { get; set; } = new();
}

public class PayPalVaultTokenResponse
{
    public string Id { get; set; } = string.Empty;
    public PayPalVaultPaymentSourceResponse? PaymentSource { get; set; }
}

public class PayPalVaultPaymentSourceResponse
{
    public PayPalCardResponse? Card { get; set; }
}

public class PayPalTransactionSearchResponse
{
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    public int Page { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public class PayPalTransactionDetail
{
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

public class PayPalTransactionInfo
{
    public string? TransactionId { get; set; }
    public string? TransactionEventCode { get; set; }
    public DateTimeOffset? TransactionInitiationDate { get; set; }
    public DateTimeOffset? TransactionUpdatedDate { get; set; }
    public PayPalMoney? TransactionAmount { get; set; }
    public PayPalMoney? FeeAmount { get; set; }
    public string? TransactionStatus { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}

public class PayPalOAuthTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}

public class PayPalErrorDetail
{
    public string? Field { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}

public class PayPalErrorResponse
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
    public string? DebugId { get; set; }
}
