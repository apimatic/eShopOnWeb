using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

// DTOs for the Transaction Search API v1 (api-specs/paypal/transaction_search_v1).
namespace Microsoft.eShopWeb.Infrastructure.Payments.Dto;

/// <summary>search_response schema (subset).</summary>
public class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }

    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("last_refreshed_datetime")]
    public DateTimeOffset? LastRefreshedDatetime { get; set; }
}

/// <summary>transaction_detail schema (subset).</summary>
public class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

/// <summary>transaction_info schema (subset).</summary>
public class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("paypal_reference_id")]
    public string? PayPalReferenceId { get; set; }

    [JsonPropertyName("paypal_reference_id_type")]
    public string? PayPalReferenceIdType { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public DateTimeOffset? TransactionInitiationDate { get; set; }

    [JsonPropertyName("transaction_updated_date")]
    public DateTimeOffset? TransactionUpdatedDate { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalMoney? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalMoney? FeeAmount { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }
}
