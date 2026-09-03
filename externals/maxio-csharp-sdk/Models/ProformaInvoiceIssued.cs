using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ProformaInvoiceIssued
{
    [JsonPropertyName("uid")]
    public required string Uid { get; init; }

    [JsonPropertyName("number")]
    public required string Number { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("delivery_date")]
    public required DateTimeOffset DeliveryDate { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("due_amount")]
    public required string DueAmount { get; init; }

    [JsonPropertyName("paid_amount")]
    public required string PaidAmount { get; init; }

    [JsonPropertyName("tax_amount")]
    public required string TaxAmount { get; init; }

    [JsonPropertyName("total_amount")]
    public required string TotalAmount { get; init; }

    [JsonPropertyName("product_name")]
    public required string ProductName { get; init; }

    [JsonPropertyName("line_items")]
    public required IReadOnlyList<InvoiceLineItemEventData> LineItems { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
