using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

// DTOs for the PayPal REST API. Serialized with JsonNamingPolicy.SnakeCaseLower, so
// e.g. CurrencyCode <-> currency_code. Null values are omitted from requests.

internal class PayPalTokenResponse
{
    public string? AccessToken { get; set; }
    public int ExpiresIn { get; set; }
}

internal class PayPalErrorResponse
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal class PayPalErrorDetail
{
    public string? Issue { get; set; }
    public string? Description { get; set; }
}

internal class PayPalMoney
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal class PayPalAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

internal class PayPalCardRequest
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayPalAddress? BillingAddress { get; set; }
    public string? VaultId { get; set; }
}

internal class PayPalPaymentSourceRequest
{
    public PayPalCardRequest? Card { get; set; }
}

internal class PayPalCreateOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
}

internal class PayPalPurchaseUnitRequest
{
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public PayPalMoney Amount { get; set; } = new();
}

internal class PayPalAuthorizeOrderRequest
{
    public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal class PayPalOrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PayPalPurchaseUnitResponse>? PurchaseUnits { get; set; }
    public List<PayPalLink>? Links { get; set; }
}

internal class PayPalPurchaseUnitResponse
{
    public PayPalPaymentsResponse? Payments { get; set; }
}

internal class PayPalPaymentsResponse
{
    public List<PayPalAuthorizationResponse>? Authorizations { get; set; }
    public List<PayPalCaptureResponse>? Captures { get; set; }
}

internal class PayPalLink
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal class PayPalAuthorizationResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
    public string? ExpirationTime { get; set; }
}

internal class PayPalCaptureRequest
{
    public PayPalMoney? Amount { get; set; }
    public string? InvoiceId { get; set; }
    public bool? FinalCapture { get; set; }
}

internal class PayPalCaptureResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal class PayPalSellerReceivableBreakdown
{
    public PayPalMoney? GrossAmount { get; set; }
    public PayPalMoney? PaypalFee { get; set; }
    public PayPalMoney? NetAmount { get; set; }
}

internal class PayPalReauthorizeRequest
{
    public PayPalMoney? Amount { get; set; }
}

internal class PayPalRefundRequest
{
    public PayPalMoney? Amount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public string? NoteToPayer { get; set; }
}

internal class PayPalRefundResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
}

internal class PayPalCreatePaymentTokenRequest
{
    public PayPalPaymentSourceRequest PaymentSource { get; set; } = new();
    public PayPalCustomerRequest? Customer { get; set; }
}

internal class PayPalCustomerRequest
{
    public string? Id { get; set; }
}

internal class PayPalPaymentTokenResponse
{
    public string? Id { get; set; }
    public PayPalCustomerResponse? Customer { get; set; }
    public PayPalPaymentTokenSource? PaymentSource { get; set; }
}

internal class PayPalCustomerResponse
{
    public string? Id { get; set; }
}

internal class PayPalPaymentTokenSource
{
    public PayPalCardToken? Card { get; set; }
}

internal class PayPalCardToken
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

internal class PayPalTransactionListResponse
{
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    public int? TotalItems { get; set; }
    public int? TotalPages { get; set; }
    public int? Page { get; set; }
}

internal class PayPalTransactionDetail
{
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal class PayPalTransactionInfo
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public PayPalMoney? TransactionAmount { get; set; }
    public PayPalMoney? FeeAmount { get; set; }
    public string? TransactionInitiationDate { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}
