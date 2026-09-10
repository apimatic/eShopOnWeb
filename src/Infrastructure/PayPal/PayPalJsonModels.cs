using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Response models mapped to the PayPal OpenAPI specs (api-specs/). Only the fields this integration
// consumes are declared; PayPal may return more. Property names follow the spec's snake_case JSON.

internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

internal sealed class MoneyDto
{
    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

internal sealed class OrderResponseDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

internal sealed class SellerReceivableBreakdownDto
{
    [JsonPropertyName("gross_amount")] public MoneyDto? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public MoneyDto? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public MoneyDto? NetAmount { get; set; }
}

internal sealed class CaptureResponseDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
}

internal sealed class RefundResponseDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
}

internal sealed class AuthorizationResponseDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
}

// --- Transaction search ---

internal sealed class SearchResponseDto
{
    [JsonPropertyName("transaction_details")] public List<TransactionDetailDto>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
}

internal sealed class TransactionDetailDto
{
    [JsonPropertyName("transaction_info")] public TransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class TransactionInfoDto
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_amount")] public MoneyDto? TransactionAmount { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
}

// --- Error model (spec: error) ---

internal sealed class ErrorResponseDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<ErrorDetailDto>? Details { get; set; }
    // OAuth token errors use a different shape.
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
}

internal sealed class ErrorDetailDto
{
    [JsonPropertyName("field")] public string? Field { get; set; }
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}
