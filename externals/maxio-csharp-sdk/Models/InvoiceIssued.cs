using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record InvoiceIssued
{
    [JsonPropertyName("uid")]
    public required string Uid { get; init; }

    [JsonPropertyName("number")]
    public required string Number { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("due_date")]
    public required DateTimeOffset? DueDate { get; init; }

    /// <summary>
    /// Invoice issue date. Can be an empty string if value is missing.
    /// </summary>
    [JsonPropertyName("issue_date")]
    public required string IssueDate { get; init; }

    /// <summary>
    /// Paid date. Can be an empty string if value is missing.
    /// </summary>
    [JsonPropertyName("paid_date")]
    public required string PaidDate { get; init; }

    [JsonPropertyName("due_amount")]
    public required string DueAmount { get; init; }

    [JsonPropertyName("paid_amount")]
    public required string PaidAmount { get; init; }

    [JsonPropertyName("tax_amount")]
    public required string TaxAmount { get; init; }

    [JsonPropertyName("refund_amount")]
    public required string RefundAmount { get; init; }

    [JsonPropertyName("total_amount")]
    public required string TotalAmount { get; init; }

    [JsonPropertyName("status_amount")]
    public required string StatusAmount { get; init; }

    [JsonPropertyName("product_name")]
    public required string ProductName { get; init; }

    [JsonPropertyName("consolidation_level")]
    public required string ConsolidationLevel { get; init; }

    [JsonPropertyName("line_items")]
    public required IReadOnlyList<InvoiceLineItemEventData> LineItems { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
