using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal.Dto;

// vault_payment_tokens_v3 schemas.

internal sealed class PayPalCreatePaymentTokenRequest
{
    [JsonPropertyName("payment_source")] public PayPalVaultPaymentSource PaymentSource { get; set; } = new();
    [JsonPropertyName("customer")] public PayPalVaultCustomer? Customer { get; set; }
}

internal sealed class PayPalVaultPaymentSource
{
    [JsonPropertyName("card")] public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalVaultCustomer
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public PayPalVaultCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceResponse? PaymentSource { get; set; }
}

// transaction_search_v1 schemas.

internal sealed class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
}

internal sealed class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")] public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
    [JsonPropertyName("paypal_reference_id_type")] public string? PayPalReferenceIdType { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public DateTimeOffset? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_updated_date")] public DateTimeOffset? TransactionUpdatedDate { get; set; }
    [JsonPropertyName("transaction_amount")] public PayPalMoney? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public PayPalMoney? FeeAmount { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
}
