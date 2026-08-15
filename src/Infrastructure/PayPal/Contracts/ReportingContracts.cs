using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

// --- GET /v1/reporting/transactions (transaction_search_v1) ---

public class SearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<TransactionDetail>? TransactionDetails { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}

public class TransactionDetail
{
    [JsonPropertyName("transaction_info")]
    public TransactionInfo? TransactionInfo { get; set; }
}

public class TransactionInfo
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("transaction_amount")]
    public Money? TransactionAmount { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }

    [JsonPropertyName("paypal_reference_id")]
    public string? PayPalReferenceId { get; set; }
}
