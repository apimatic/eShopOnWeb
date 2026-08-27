using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Deserialization shapes for the PayPal payloads this integration consumes.
/// Property names follow the OpenAPI specifications in /api-specs/paypal.
/// </summary>
public class PayPalTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
}

public class PayPalMoneyDto
{
    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

public class PayPalOrderDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitDto>? PurchaseUnits { get; set; }

    public PayPalAuthorizationDto? FirstAuthorization() =>
        PurchaseUnits?.SelectMany(u => u.Payments?.Authorizations ?? new List<PayPalAuthorizationDto>()).FirstOrDefault();
}

public class PayPalPurchaseUnitDto
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("payments")] public PayPalPaymentsDto? Payments { get; set; }
}

public class PayPalPaymentsDto
{
    [JsonPropertyName("authorizations")] public List<PayPalAuthorizationDto>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<PayPalCaptureDto>? Captures { get; set; }
    [JsonPropertyName("refunds")] public List<PayPalRefundDto>? Refunds { get; set; }
}

public class PayPalAuthorizationDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoneyDto? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
}

public class PayPalCaptureDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoneyDto? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public PayPalSellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
}

public class PayPalSellerReceivableBreakdownDto
{
    [JsonPropertyName("gross_amount")] public PayPalMoneyDto? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PayPalMoneyDto? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public PayPalMoneyDto? NetAmount { get; set; }
}

public class PayPalRefundDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoneyDto? Amount { get; set; }
}

public class PayPalVaultTokenDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("payment_source")] public PayPalVaultPaymentSourceDto? PaymentSource { get; set; }
}

public class PayPalVaultPaymentSourceDto
{
    [JsonPropertyName("card")] public PayPalVaultCardDto? Card { get; set; }
}

public class PayPalVaultCardDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

public class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<PayPalTransactionDetailDto>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
}

public class PayPalTransactionDetailDto
{
    [JsonPropertyName("transaction_info")] public PayPalTransactionInfoDto? TransactionInfo { get; set; }
}

public class PayPalTransactionInfoDto
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
    [JsonPropertyName("paypal_reference_id_type")] public string? PayPalReferenceIdType { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_updated_date")] public string? TransactionUpdatedDate { get; set; }
    [JsonPropertyName("transaction_amount")] public PayPalMoneyDto? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public PayPalMoneyDto? FeeAmount { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
}

public class PayPalErrorDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetailDto>? Details { get; set; }
}

public class PayPalErrorDetailDto
{
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("field")] public string? Field { get; set; }
}
