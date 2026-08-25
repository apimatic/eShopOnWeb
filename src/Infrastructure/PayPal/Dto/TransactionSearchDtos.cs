using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Dto;

// transaction_search_v1: GET /v1/reporting/transactions

public class TransactionSearchResponseDto
{
    [JsonPropertyName("transaction_details")] public List<TransactionDetailDto>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
}

public class TransactionDetailDto
{
    [JsonPropertyName("transaction_info")] public TransactionInfoDto? TransactionInfo { get; set; }
}

public class TransactionInfoDto
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
    [JsonPropertyName("paypal_reference_id_type")] public string? PayPalReferenceIdType { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public DateTimeOffset? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_updated_date")] public DateTimeOffset? TransactionUpdatedDate { get; set; }
    [JsonPropertyName("transaction_amount")] public AmountDto? TransactionAmount { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
}
