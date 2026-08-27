using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

// DTOs mirroring the PayPal OpenAPI schemas in api-specs/paypal (checkout_orders_v2,
// payments_payment_v2, vault_payment_tokens_v3, transaction_search_v1).
// Serialized with JsonNamingPolicy.SnakeCaseLower; explicit JsonPropertyName is used
// where the policy would diverge from the spec's field name.

internal class PayPalMoney
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal class PayPalAddress
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string CountryCode { get; set; } = "US";
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

internal class PayPalPurchaseUnitRequest
{
    public string? ReferenceId { get; set; }
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
    public string? Description { get; set; }
    public PayPalMoney Amount { get; set; } = new PayPalMoney();
}

internal class PayPalOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new List<PayPalPurchaseUnitRequest>();
    public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal class PayPalAuthorization
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
    public DateTimeOffset? ExpirationTime { get; set; }
}

internal class PayPalSellerReceivableBreakdown
{
    public PayPalMoney? GrossAmount { get; set; }
    public PayPalMoney? PaypalFee { get; set; }
    public PayPalMoney? NetAmount { get; set; }
}

internal class PayPalCapture
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
    public bool? FinalCapture { get; set; }
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal class PayPalRefund
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
}

internal class PayPalPaymentCollection
{
    public List<PayPalAuthorization>? Authorizations { get; set; }
    public List<PayPalCapture>? Captures { get; set; }
    public List<PayPalRefund>? Refunds { get; set; }
}

internal class PayPalPurchaseUnit
{
    public string? ReferenceId { get; set; }
    public PayPalPaymentCollection? Payments { get; set; }
}

internal class PayPalOrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
}

internal class PayPalCaptureRequest
{
    public PayPalMoney? Amount { get; set; }
    public bool FinalCapture { get; set; } = true;
    public string? InvoiceId { get; set; }
    public string? NoteToPayer { get; set; }
}

internal class PayPalReauthorizeRequest
{
    public PayPalMoney Amount { get; set; } = new PayPalMoney();
}

internal class PayPalRefundRequest
{
    public PayPalMoney? Amount { get; set; }
    public string? CustomId { get; set; }
    public string? NoteToPayer { get; set; }
}

internal class PayPalTokenResponse
{
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}

internal class PayPalErrorDetail
{
    public string? Issue { get; set; }
    public string? Description { get; set; }
    public string? Field { get; set; }
}

internal class PayPalError
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
    public string? DebugId { get; set; }
}

internal class PayPalVaultCustomer
{
    public string Id { get; set; } = string.Empty;
}

internal class PayPalPaymentTokenRequest
{
    public PayPalVaultCustomer? Customer { get; set; }
    public PayPalPaymentSourceRequest PaymentSource { get; set; } = new PayPalPaymentSourceRequest();
}

internal class PayPalVaultCard
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

internal class PayPalVaultPaymentSource
{
    public PayPalVaultCard? Card { get; set; }
}

internal class PayPalPaymentTokenResponse
{
    public string? Id { get; set; }
    public PayPalVaultPaymentSource? PaymentSource { get; set; }
}

internal class PayPalTransactionInfoDto
{
    public string? TransactionId { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public PayPalMoney? TransactionAmount { get; set; }
    public PayPalMoney? FeeAmount { get; set; }
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? PaypalReferenceIdType { get; set; }
    public DateTimeOffset? TransactionInitiationDate { get; set; }
    public DateTimeOffset? TransactionUpdatedDate { get; set; }
}

internal class PayPalTransactionDetail
{
    public PayPalTransactionInfoDto? TransactionInfo { get; set; }
}

internal class PayPalTransactionSearchResponse
{
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
}
